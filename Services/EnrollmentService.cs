namespace CapYdp.Services;

/// <summary>
/// Dönemlik ders kayıtları (denemeler) ve dönem sonu not girişleri.
/// Onay mantığı:
///  - Öğrencinin girdiği kayıt "onay bekliyor" ile başlar, onaylanınca kilitlenir.
///  - Yalnızca koordinatör düzeltir; her düzeltme eski/yeni değeri ve gerekçesiyle kaydedilir.
///  - Reddedilen / iptal edilen kayıt silinmez.
/// </summary>
public static class EnrollmentService
{
    private static decimal? GradePoint(Dictionary<string, decimal> scale, string? grade)
    {
        if (grade != null && scale.TryGetValue(grade, out var gp))
        {
            return gp;
        }
        return null;
    }

    public static readonly string[] Statuses = { "planlandi", "alindi", "gecti", "kaldi", "muaf", "cikarildi" };

    /// <summary>Öğrencinin dönem dersi girişi</summary>
    public static long StudentAdd(IAppCtx c, long studentId, long courseId, long termId)
    {
        var st = c.Db.One("SELECT * FROM students WHERE id = @id", new { id = studentId })!;
        if (st.S("status") != "aktif")
        {
            throw new UserError("Kaydınız aktif olmadığı için ders girişi yapamazsınız.");
        }
        var term = Terms.Find(c, termId);
        var cur = Terms.Current(c);
        if (term == null || !term.B("is_active"))
        {
            throw new UserError("Dönem seçilmelidir.");
        }
        if (cur != null && term.I("sort_key") < cur.I("sort_key"))
        {
            throw new UserError("Geçmiş dönemlere ders girişi yalnızca koordinatör tarafından yapılabilir.");
        }
        // Serbest metinle ders uydurulamaz: yalnızca onaylı ders listesindeki dersler
        if (c.Db.Scalar("SELECT id FROM student_courses WHERE student_id = @sid AND course_id = @cid AND state = 'onaylandi'", new { sid = studentId, cid = courseId }) == null)
        {
            throw new UserError("Dönem dersi olarak yalnızca onaylı ders listenizdeki dersleri seçebilirsiniz. Önce dersi listenize ekleyip onay alın.");
        }
        if (c.Db.Scalar("SELECT id FROM enrollments WHERE student_id = @sid AND course_id = @cid AND approval_state = 'onaylandi' AND status IN ('gecti','muaf')", new { sid = studentId, cid = courseId }) != null)
        {
            throw new UserError("Bu dersi zaten geçtiniz / muafsınız.");
        }
        if (c.Db.Scalar("SELECT id FROM enrollments WHERE student_id = @sid AND course_id = @cid AND term_id = @tid AND approval_state IN ('onay_bekliyor','onaylandi') AND status <> 'cikarildi'",
                new { sid = studentId, cid = courseId, tid = termId }) != null)
        {
            throw new UserError("Bu ders bu dönem için zaten girilmiş.");
        }
        var status = cur != null && term.I("sort_key") > cur.I("sort_key") ? "planlandi" : "alindi";
        var id = Insert(c, studentId, courseId, termId, status, null, "onay_bekliyor", false, null);
        var code = c.Db.Str("SELECT code FROM courses WHERE id = @id", new { id = courseId });
        Notify.Coordinators(c, st.S("full_name") + " " + Terms.Label(term) + " dönemi için " + code + " dersini girdi (onay bekliyor).", "/koordinator/ogrenci/" + studentId + "?tab=dersler");
        return id;
    }

    /// <summary>
    /// Koordinatör girişi: doğrudan onaylı. Geriye dönük (mevcut öğrenci) kayıtlar da buradan girilir
    /// ve onay kilidine takılmaz.
    /// </summary>
    public static long CoordinatorAdd(IAppCtx c, long studentId, long courseId, long termId, string status, string? grade, bool legacy, string note = "")
    {
        if (!Statuses.Contains(status))
        {
            throw new UserError("Geçersiz ders durumu.");
        }
        if (Terms.Find(c, termId) == null)
        {
            throw new UserError("Dönem seçilmelidir.");
        }
        if (c.Db.Scalar("SELECT id FROM courses WHERE id = @id", new { id = courseId }) == null)
        {
            throw new UserError("Ders katalogda bulunamadı.");
        }
        grade = NormalizeGrade(c, studentId, grade, status);
        var id = Insert(c, studentId, courseId, termId, status, grade, "onaylandi", legacy, string.IsNullOrWhiteSpace(note) ? null : note);
        // Geriye dönük girişte ders listede yoksa onaylı olarak eklenir
        if (c.Db.Scalar("SELECT id FROM student_courses WHERE student_id = @sid AND course_id = @cid AND state IN ('onaylandi','onay_bekliyor')", new { sid = studentId, cid = courseId }) == null)
        {
            CourseService.PlanAdd(c, studentId, courseId, legacy ? "Geriye dönük kayıt ile eklendi" : "Dönem kaydı ile eklendi", legacy);
        }
        Rules.Snapshot(c, studentId, legacy ? "Geriye dönük ders girişi" : "Koordinatör ders girişi", false);
        return id;
    }

    private static long Insert(IAppCtx c, long studentId, long courseId, long termId, string status, string? grade, string approval, bool legacy, string? note)
    {
        var u = c.User!;
        var attempt = 1 + c.Db.Count("SELECT COUNT(*) FROM enrollments WHERE student_id = @sid AND course_id = @cid AND approval_state IN ('onaylandi','onay_bekliyor') AND status <> 'cikarildi'",
            new { sid = studentId, cid = courseId });
        var type = c.Db.Str("SELECT program_type FROM students WHERE id = @id", new { id = studentId })!;
        var scale = Rules.GradeScale(c, type);
        var approved = approval == "onaylandi";
        var id = c.Db.Insert("enrollments", new Data
        {
            ["student_id"] = studentId,
            ["course_id"] = courseId,
            ["term_id"] = termId,
            ["attempt_no"] = (int)attempt,
            ["status"] = status,
            ["letter_grade"] = grade,
            ["grade_point"] = GradePoint(scale, grade),
            ["passed_term_id"] = status is "gecti" or "muaf" ? termId : null,
            ["approval_state"] = approval,
            ["note"] = note,
            ["is_legacy"] = legacy ? 1 : 0,
            ["created_by"] = u.Id,
            ["created_at"] = Clock.Now(),
            ["approved_by"] = approved ? u.Id : null,
            ["approved_at"] = approved ? Clock.Now() : null,
            ["updated_at"] = Clock.Now(),
        });
        var code = c.Db.Str("SELECT code FROM courses WHERE id = @id", new { id = courseId });
        Audit.Log(c, "enrollment_add", "enrollment", id, studentId, null, new Data
        {
            ["ders"] = code, ["donem"] = Terms.Label(Terms.Find(c, termId)), ["durum"] = status, ["not"] = grade,
            ["onay"] = approval, ["geriye_donuk"] = legacy ? "evet" : "hayır",
        });
        return id;
    }

    private static string? NormalizeGrade(IAppCtx c, long studentId, string? grade, string status)
    {
        grade = grade?.Trim().ToUpperInvariant();
        if (grade == "")
        {
            grade = null;
        }
        var type = c.Db.Str("SELECT program_type FROM students WHERE id = @id", new { id = studentId })!;
        var p = Rules.ActiveSet(c, type).Params;
        var pass = p.PassGrades;
        if (grade != null && !p.Scale.ContainsKey(grade))
        {
            throw new UserError("Geçersiz harf notu: " + grade);
        }
        if (status is "gecti" or "kaldi" && grade == null)
        {
            throw new UserError("\"Geçti\" veya \"Kaldı\" durumunda harf notu girilmelidir.");
        }
        if (status == "gecti" && !pass.Contains(grade!))
        {
            throw new UserError($"{grade} geçer not değil; durum \"Kaldı\" olmalıdır.");
        }
        if (status == "kaldi" && pass.Contains(grade!))
        {
            throw new UserError($"{grade} geçer bir not; durum \"Geçti\" olmalıdır.");
        }
        if (status is "planlandi" or "alindi" or "cikarildi")
        {
            grade = null;
        }
        return grade;
    }

    public static long Decide(IAppCtx c, long id, bool approve, string reason = "", bool snapshot = true)
    {
        var e = c.Db.One("SELECT e.*, c.code FROM enrollments e JOIN courses c ON c.id = e.course_id WHERE e.id = @id AND e.approval_state = 'onay_bekliyor'", new { id })
                ?? throw new UserError("Onay bekleyen kayıt bulunamadı.");
        if (!approve)
        {
            StudentService.RequireReason(reason);
        }
        var state = approve ? "onaylandi" : "reddedildi";
        c.Db.Update("enrollments", new Data
        {
            ["approval_state"] = state, ["approved_by"] = c.User!.Id, ["approved_at"] = Clock.Now(),
            ["reject_reason"] = approve ? null : reason, ["updated_at"] = Clock.Now(),
        }, "id = @id", new { id });
        Audit.Log(c, approve ? "enrollment_approve" : "enrollment_reject", "enrollment", id, e.L("student_id"),
            new Data { ["onay"] = "onay_bekliyor" }, new Data { ["onay"] = state, ["ders"] = e.S("code") }, reason == "" ? null : reason);
        Notify.Student(c, e.L("student_id"), "Dönem dersi " + e.S("code") + " " + (approve ? "onaylandı." : "reddedildi. Gerekçe: " + reason), "/ogrenci/dersler");
        if (approve && snapshot)
        {
            Rules.Snapshot(c, e.L("student_id"), "Dönem dersi onayı", false);
        }
        return e.L("student_id");
    }

    /// <summary>Koordinatör düzeltmesi (onaylı kayıtta da yapılabilir; gerekçe zorunlu, eski/yeni değer kaydedilir)</summary>
    public static long Correct(IAppCtx c, long id, long termId, string status, string? grade, string reason)
    {
        var e = c.Db.One("SELECT * FROM enrollments WHERE id = @id", new { id });
        if (e == null || e.S("approval_state") is "iptal" or "reddedildi")
        {
            throw new UserError("Düzeltilebilir kayıt bulunamadı.");
        }
        StudentService.RequireReason(reason);
        if (!Statuses.Contains(status) || Terms.Find(c, termId) == null)
        {
            throw new UserError("Durum veya dönem geçersiz.");
        }
        var sid = e.L("student_id");
        grade = NormalizeGrade(c, sid, grade, status);
        var type = c.Db.Str("SELECT program_type FROM students WHERE id = @id", new { id = sid })!;
        var scale = Rules.GradeScale(c, type);
        long? passedTerm = null;
        if (status is "gecti" or "muaf")
        {
            passedTerm = !e.IsNull("passed_term_id") && e.S("status") == status ? e.L("passed_term_id") : termId;
        }
        var nw = new Data
        {
            ["term_id"] = termId,
            ["status"] = status,
            ["letter_grade"] = grade,
            ["grade_point"] = GradePoint(scale, grade),
            ["passed_term_id"] = passedTerm,
        };
        var (o, n) = Audit.Diff(e, nw);
        if (n.Count == 0)
        {
            throw new UserError("Herhangi bir değişiklik yapılmadı.");
        }
        n["correction_count"] = e.I("correction_count") + 1;
        n["updated_at"] = Clock.Now();
        c.Db.Update("enrollments", n, "id = @id", new { id });
        Audit.Log(c, "enrollment_correct", "enrollment", id, sid, o, n, reason);
        Notify.Student(c, sid, "Bir ders kaydınız koordinatör tarafından düzeltildi. Gerekçe: " + reason, "/ogrenci/dersler");
        Rules.Snapshot(c, sid, "Ders kaydı düzeltmesi");
        return sid;
    }

    /// <summary>Şartlara uymayan kaydı iptal etme (kayıt silinmez)</summary>
    public static long Cancel(IAppCtx c, long id, string reason)
    {
        var e = c.Db.One("SELECT e.*, c.code FROM enrollments e JOIN courses c ON c.id = e.course_id WHERE e.id = @id AND e.approval_state IN ('onay_bekliyor','onaylandi')", new { id })
                ?? throw new UserError("İptal edilebilir kayıt bulunamadı.");
        StudentService.RequireReason(reason);
        c.Db.Update("enrollments", new Data { ["approval_state"] = "iptal", ["cancel_reason"] = reason, ["updated_at"] = Clock.Now() }, "id = @id", new { id });
        c.Db.Exec("UPDATE grade_submissions SET state = 'iptal', reject_reason = @r WHERE enrollment_id = @id AND state = 'onay_bekliyor'", new { r = "Ders kaydı iptal edildi", id });
        Audit.Log(c, "enrollment_cancel", "enrollment", id, e.L("student_id"), new Data { ["onay"] = e.S("approval_state") }, new Data { ["onay"] = "iptal", ["ders"] = e.S("code") }, reason);
        Notify.Student(c, e.L("student_id"), "Ders kaydınız iptal edildi (" + e.S("code") + "). Gerekçe: " + reason, "/ogrenci/dersler");
        Rules.Snapshot(c, e.L("student_id"), "Ders kaydı iptali", false);
        return e.L("student_id");
    }

    public static void Withdraw(IAppCtx c, long id, long studentId)
    {
        _ = c.Db.One("SELECT * FROM enrollments WHERE id = @id AND student_id = @sid AND approval_state = 'onay_bekliyor'", new { id, sid = studentId })
            ?? throw new UserError("Yalnızca onay bekleyen kendi kayıtlarınızı geri çekebilirsiniz.");
        c.Db.Update("enrollments", new Data { ["approval_state"] = "iptal", ["cancel_reason"] = "Öğrenci tarafından geri çekildi", ["updated_at"] = Clock.Now() }, "id = @id", new { id });
        Audit.Log(c, "enrollment_withdraw", "enrollment", id, studentId, new Data { ["onay"] = "onay_bekliyor" }, new Data { ["onay"] = "iptal" });
    }

    // ================= Dönem sonu not girişi =================

    /// <summary>Öğrencinin not girişi yapabileceği (onaylı, notu girilmemiş) dersler</summary>
    public static List<Row> GradeableFor(IAppCtx c, long studentId, long termId)
    {
        return c.Db.All(@"SELECT e.id, e.attempt_no, c.code, c.name, c.ects,
                   (SELECT gs.letter_grade FROM grade_submissions gs WHERE gs.enrollment_id = e.id AND gs.state = 'onay_bekliyor' ORDER BY gs.id DESC LIMIT 1) AS pending_grade,
                   (SELECT gs.reject_reason FROM grade_submissions gs WHERE gs.enrollment_id = e.id AND gs.state = 'reddedildi' ORDER BY gs.id DESC LIMIT 1) AS last_reject
                FROM enrollments e JOIN courses c ON c.id = e.course_id
                WHERE e.student_id = @sid AND e.term_id = @tid AND e.approval_state = 'onaylandi' AND e.status = 'alindi'
                ORDER BY c.code", new { sid = studentId, tid = termId });
    }

    /// <summary>Dönem transkripti (ÇAP/YDP not belgesi) yüklenmeden notlar onaya gönderilemez</summary>
    public static int SubmitGrades(IAppCtx c, long studentId, long termId, Dictionary<long, string> grades)
    {
        var doc = c.Db.One("SELECT id FROM documents WHERE student_id = @sid AND doc_type = 'cap_ydp_transkript' AND term_id = @tid AND state IN ('bekliyor','onaylandi') ORDER BY id DESC LIMIT 1",
            new { sid = studentId, tid = termId });
        if (doc == null)
        {
            throw new UserError("Notlarınızı onaya göndermeden önce bu dönemin ÇAP/YDP not belgesini (transkript) yüklemelisiniz.");
        }
        var type = c.Db.Str("SELECT program_type FROM students WHERE id = @id", new { id = studentId })!;
        var scale = Rules.GradeScale(c, type);
        var allowed = GradeableFor(c, studentId, termId).ToDictionary(r => r.L("id"));
        var n = 0;
        c.Db.Tx(() =>
        {
            foreach (var kv in grades)
            {
                var g = (kv.Value ?? "").Trim().ToUpperInvariant();
                if (g == "")
                {
                    continue;
                }
                if (!allowed.TryGetValue(kv.Key, out var row))
                {
                    throw new UserError("Not girilemeyecek bir ders seçildi.");
                }
                if (!scale.ContainsKey(g))
                {
                    throw new UserError("Geçersiz harf notu: " + g);
                }
                if (!row.IsNull("pending_grade"))
                {
                    continue; // zaten onay bekliyor
                }
                var id = c.Db.Insert("grade_submissions", new Data
                {
                    ["enrollment_id"] = kv.Key,
                    ["student_id"] = studentId,
                    ["term_id"] = termId,
                    ["letter_grade"] = g,
                    ["transcript_document_id"] = doc.L("id"),
                    ["state"] = "onay_bekliyor",
                    ["submitted_by"] = c.User!.Id,
                    ["created_at"] = Clock.Now(),
                });
                Audit.Log(c, "grade_submit", "grade_submission", id, studentId, null, new Data { ["ders"] = row.S("code"), ["not"] = g });
                n++;
            }
        });
        if (n == 0)
        {
            throw new UserError("Onaya gönderilecek yeni not girilmedi.");
        }
        var st = c.Db.One("SELECT full_name FROM students WHERE id = @id", new { id = studentId })!;
        Notify.Coordinators(c, st.S("full_name") + $" {n} dersin dönem sonu notunu girdi (onay bekliyor).", "/koordinator/ogrenci/" + studentId + "?tab=notlar");
        return n;
    }

    public static long DecideGrade(IAppCtx c, long id, bool approve, string reason = "", bool snapshot = true)
    {
        var g = c.Db.One(@"SELECT gs.*, e.status, e.course_id, c.code FROM grade_submissions gs JOIN enrollments e ON e.id = gs.enrollment_id JOIN courses c ON c.id = e.course_id
                           WHERE gs.id = @id AND gs.state = 'onay_bekliyor'", new { id }) ?? throw new UserError("Onay bekleyen not bulunamadı.");
        if (!approve)
        {
            StudentService.RequireReason(reason);
        }
        var sid = g.L("student_id");
        c.Db.Tx(() =>
        {
            c.Db.Update("grade_submissions", new Data
            {
                ["state"] = approve ? "onaylandi" : "reddedildi", ["reject_reason"] = approve ? null : reason,
                ["decided_by"] = c.User!.Id, ["decided_at"] = Clock.Now(),
            }, "id = @id", new { id });
            if (approve)
            {
                var type = c.Db.Str("SELECT program_type FROM students WHERE id = @id", new { id = sid })!;
                var p = Rules.ActiveSet(c, type).Params;
                var letter = g.S("letter_grade");
                var pass = p.PassGrades.Contains(letter);
                var nw = new Data
                {
                    ["letter_grade"] = letter,
                    ["grade_point"] = GradePoint(p.Scale, letter),
                    ["status"] = pass ? "gecti" : "kaldi",
                    ["passed_term_id"] = pass ? g.L("term_id") : null,
                    ["updated_at"] = Clock.Now(),
                };
                var old = c.Db.One("SELECT letter_grade, status, passed_term_id FROM enrollments WHERE id = @id", new { id = g.L("enrollment_id") })!;
                c.Db.Update("enrollments", nw, "id = @id", new { id = g.L("enrollment_id") });
                var logged = new Data(nw) { ["ders"] = g.S("code") };
                Audit.Log(c, "grade_approve", "enrollment", g.L("enrollment_id"), sid, old.ToDict(), logged);
            }
            else
            {
                Audit.Log(c, "grade_reject", "grade_submission", id, sid, new Data { ["state"] = "onay_bekliyor" },
                    new Data { ["state"] = "reddedildi", ["ders"] = g.S("code"), ["not"] = g.S("letter_grade") }, reason);
            }
        });
        Notify.Student(c, sid, g.S("code") + " notunuz (" + g.S("letter_grade") + ") " + (approve ? "onaylandı." : "reddedildi. Gerekçe: " + reason), "/ogrenci/notlar");
        if (approve && snapshot)
        {
            Rules.Snapshot(c, sid, "Not onayı");
        }
        return sid;
    }
}
