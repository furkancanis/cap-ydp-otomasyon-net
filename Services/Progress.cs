namespace CapYdp.Services;

/// <summary>Ders listesindeki bir dersin ilerleme durumu</summary>
public sealed class PlanCourse
{
    public long PlanId { get; init; }
    public long CourseId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public decimal Ects { get; init; }
    public int? Semester { get; init; }
    public string Category { get; init; } = "";
    public bool IsLegacy { get; init; }
    public int AttemptCount { get; set; }
    /// <summary>alinmadi | devam | planlandi | kaldi | gecti | muaf</summary>
    public string State { get; set; } = "alinmadi";
    public string? Grade { get; set; }
    public string? PassedTerm { get; set; }
}

public sealed record TermAvg(int SortKey, string Term, decimal? Avg);

public sealed class ProgressInfo
{
    public List<PlanCourse> Plan { get; } = new();
    public List<Row> Attempts { get; set; } = new();
    public int PlanCount => Plan.Count;
    public decimal PlanEcts { get; set; }
    public int CompletedCount { get; set; }
    public decimal CompletedEcts { get; set; }
    public int RemainingCount { get; set; }
    public decimal RemainingEcts { get; set; }
    public List<Row> OffPlanCompleted { get; set; } = new();
    public decimal? Gpa { get; set; }
    public List<TermAvg> TermCumulative { get; } = new();
    public int ConditionalCount { get; set; }
    public Dictionary<int, int> ElectivesBySem { get; } = new();
    public Dictionary<string, long> Pending { get; } = new();
    public long PendingTotal => Pending.Values.Sum();
    public int Percent => PlanEcts > 0 ? (int)Math.Min(100, Math.Round(100 * (PlanEcts - RemainingEcts) / PlanEcts)) : 0;
}

/// <summary>Öğrencinin ders ilerlemesini hesaplar (yalnızca ONAYLI kayıtlar üzerinden)</summary>
public static class Progress
{
    public static ProgressInfo Compute(IAppCtx c, long studentId, RuleParams? p = null)
    {
        if (p == null)
        {
            var type = c.Db.Str("SELECT program_type FROM students WHERE id = @id", new { id = studentId }) ?? "CAP";
            p = Rules.ActiveSet(c, type).Params;
        }
        var scale = p.Scale;
        var condGrades = p.CondGrades;
        var info = new ProgressInfo();

        var plan = c.Db.All(@"SELECT sc.id AS plan_id, sc.state, sc.is_legacy, c.id AS course_id, c.code, c.name, c.ects, c.semester, c.category
                              FROM student_courses sc JOIN courses c ON c.id = sc.course_id
                              WHERE sc.student_id = @sid AND sc.state = 'onaylandi' ORDER BY c.semester, c.code", new { sid = studentId });

        info.Attempts = c.Db.All(@"SELECT e.*, c.code, c.name, c.ects, c.credit, c.semester, t.academic_year, t.season, t.sort_key,
                                          pt.academic_year AS passed_year, pt.season AS passed_season
                                   FROM enrollments e JOIN courses c ON c.id = e.course_id JOIN terms t ON t.id = e.term_id
                                   LEFT JOIN terms pt ON pt.id = e.passed_term_id
                                   WHERE e.student_id = @sid ORDER BY t.sort_key, c.code, e.attempt_no", new { sid = studentId });

        var approved = info.Attempts.Where(a => a.S("approval_state") == "onaylandi").ToList();
        var byCourse = approved.GroupBy(a => a.L("course_id")).ToDictionary(g => g.Key, g => g.ToList());

        // ---- plan derslerinin durumu ----
        var planIds = new HashSet<long>();
        foreach (var r in plan)
        {
            var pc = new PlanCourse
            {
                PlanId = r.L("plan_id"),
                CourseId = r.L("course_id"),
                Code = r.S("code"),
                Name = r.S("name"),
                Ects = r.Dec("ects"),
                Semester = r.IsNull("semester") ? null : r.I("semester"),
                Category = r.S("category"),
                IsLegacy = r.B("is_legacy"),
            };
            planIds.Add(pc.CourseId);
            info.PlanEcts += pc.Ects;
            if (pc.Category == "teknik_secmeli" && pc.Semester != null)
            {
                info.ElectivesBySem[pc.Semester.Value] = (info.ElectivesBySem.TryGetValue(pc.Semester.Value, out var n) ? n : 0) + 1;
            }
            var tries = byCourse.TryGetValue(pc.CourseId, out var tl) ? tl : new List<Row>();
            pc.AttemptCount = tries.Count(t => t.S("status") != "cikarildi");
            foreach (var t in tries)
            {
                var st = t.S("status");
                if (st is "gecti" or "muaf")
                {
                    pc.State = st;
                    pc.Grade = t.Sn("letter_grade");
                    pc.PassedTerm = !t.IsNull("passed_year") ? t.S("passed_year") + " " + t.S("passed_season") : t.S("academic_year") + " " + t.S("season");
                }
            }
            if (pc.State is not ("gecti" or "muaf") && tries.Count > 0)
            {
                var last = tries[^1];
                pc.State = last.S("status") switch
                {
                    "alindi" => "devam",
                    "planlandi" => "planlandi",
                    "kaldi" => "kaldi",
                    _ => "alinmadi",
                };
                pc.Grade = last.Sn("letter_grade");
            }
            if (pc.State is not ("gecti" or "muaf"))
            {
                info.RemainingCount++;
                info.RemainingEcts += pc.Ects;
            }
            info.Plan.Add(pc);
        }

        // ---- tamamlanan dersler (plandan çıkarılmış olsa da geçilmiş dersler korunur) ----
        var completed = new Dictionary<long, Row>();
        foreach (var kv in byCourse)
        {
            foreach (var t in kv.Value)
            {
                if (t.S("status") is "gecti" or "muaf")
                {
                    completed[kv.Key] = t;
                }
            }
        }
        info.CompletedCount = completed.Count;
        info.CompletedEcts = completed.Values.Sum(t => t.Dec("ects"));
        info.OffPlanCompleted = completed.Values.Where(t => !planIds.Contains(t.L("course_id"))).ToList();

        // ---- not ortalaması (her dersin son notlu denemesi, AKTS ağırlıklı) ----
        var graded = approved.Where(a => a.S("status") is "gecti" or "kaldi" && !a.IsNull("letter_grade") && scale.ContainsKey(a.S("letter_grade"))).ToList();
        decimal? GpaOf(IEnumerable<Row> rows)
        {
            var latest = new Dictionary<long, Row>();
            foreach (var r in rows)
            {
                latest[r.L("course_id")] = r; // sıralı olduğundan son deneme kalır
            }
            decimal sum = 0, w = 0;
            foreach (var r in latest.Values)
            {
                sum += scale[r.S("letter_grade")] * r.Dec("ects");
                w += r.Dec("ects");
            }
            return w > 0 ? Math.Round(sum / w, 2, MidpointRounding.AwayFromZero) : null;
        }
        info.Gpa = GpaOf(graded);
        foreach (var k in graded.Select(g => g.I("sort_key")).Distinct().OrderBy(x => x))
        {
            var first = graded.First(g => g.I("sort_key") == k);
            info.TermCumulative.Add(new TermAvg(k, first.S("academic_year") + " " + first.S("season"), GpaOf(graded.Where(g => g.I("sort_key") <= k))));
        }
        var latestGraded = new Dictionary<long, Row>();
        foreach (var g in graded)
        {
            latestGraded[g.L("course_id")] = g;
        }
        info.ConditionalCount = latestGraded.Values.Count(g => condGrades.Contains(g.S("letter_grade")) && g.S("status") == "gecti");

        var sid = new { sid = studentId };
        info.Pending["plan"] = c.Db.Count("SELECT COUNT(*) FROM student_courses WHERE student_id = @sid AND state = 'onay_bekliyor'", sid);
        info.Pending["enrollments"] = c.Db.Count("SELECT COUNT(*) FROM enrollments WHERE student_id = @sid AND approval_state = 'onay_bekliyor'", sid);
        info.Pending["grades"] = c.Db.Count("SELECT COUNT(*) FROM grade_submissions WHERE student_id = @sid AND state = 'onay_bekliyor'", sid);
        info.Pending["documents"] = c.Db.Count("SELECT COUNT(*) FROM documents WHERE student_id = @sid AND state = 'bekliyor'", sid);
        info.Pending["requests"] = c.Db.Count("SELECT COUNT(*) FROM document_requests WHERE student_id = @sid AND state IN ('bekliyor','yuklendi')", sid);
        info.Pending["course_requests"] = c.Db.Count("SELECT COUNT(*) FROM course_requests WHERE student_id = @sid AND state = 'bekliyor'", sid);
        return info;
    }
}
