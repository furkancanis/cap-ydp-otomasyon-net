using System.Text;
using CapYdp.Web;
using Microsoft.AspNetCore.Mvc;

namespace CapYdp.Controllers;

public sealed class CoordinatorController : AppController
{
    // ======================= Panolar =======================

    private (Dictionary<string, Dictionary<string, long>> Counts, Dictionary<string, long> Pending, List<(Row Student, List<RuleItem> Items)> Alerts) Overview()
    {
        var counts = new Dictionary<string, Dictionary<string, long>> { ["CAP"] = new(), ["YDP"] = new() };
        foreach (var r in C.Db.All("SELECT program_type, status, COUNT(*) AS n FROM students GROUP BY program_type, status"))
        {
            counts[r.S("program_type")][r.S("status")] = r.L("n");
        }
        var pending = new Dictionary<string, long>
        {
            ["plan"] = C.Db.Count("SELECT COUNT(*) FROM student_courses WHERE state = 'onay_bekliyor'"),
            ["enrollments"] = C.Db.Count("SELECT COUNT(*) FROM enrollments WHERE approval_state = 'onay_bekliyor'"),
            ["grades"] = C.Db.Count("SELECT COUNT(*) FROM grade_submissions WHERE state = 'onay_bekliyor'"),
            ["documents"] = C.Db.Count("SELECT COUNT(*) FROM documents WHERE state = 'bekliyor'"),
            ["course_requests"] = C.Db.Count("SELECT COUNT(*) FROM course_requests WHERE state = 'bekliyor'"),
        };
        var alerts = new List<(Row, List<RuleItem>)>();
        foreach (var a in C.Db.All(@"SELECT s.id, s.student_no, s.full_name, s.program_type, rc.summary_level, rc.results, rc.created_at
                FROM students s JOIN rule_checks rc ON rc.id = (SELECT MAX(id) FROM rule_checks WHERE student_id = s.id)
                WHERE rc.summary_level IN ('uyari','kritik') AND s.status IN ('aktif','donduruldu')
                ORDER BY CASE rc.summary_level WHEN 'kritik' THEN 0 ELSE 1 END, s.full_name"))
        {
            alerts.Add((a, Rules.ItemsOf(a.S("results")).Where(i => i.Level is "uyari" or "kritik").ToList()));
        }
        return (counts, pending, alerts);
    }

    [HttpGet("/koordinator"), Roles("koordinator")]
    public IActionResult Dashboard()
    {
        var (counts, pending, alerts) = Overview();
        Title("Koordinatör Paneli");
        ViewBag.Counts = counts;
        ViewBag.Pending = pending;
        ViewBag.Alerts = alerts;
        ViewBag.Unread = C.Db.All(@"SELECT s.id, s.full_name, s.student_no, COUNT(*) AS n, MAX(m.created_at) AS last_at FROM messages m JOIN students s ON s.id = m.student_id
            WHERE m.sender_role = 'ogrenci' AND m.read_at IS NULL GROUP BY s.id, s.full_name, s.student_no ORDER BY last_at DESC");
        ViewBag.Recent = C.Db.All("SELECT a.*, s.full_name AS student_name FROM audit_log a LEFT JOIN students s ON s.id = a.student_id ORDER BY a.id DESC LIMIT 12");
        return View("~/Views/Coord/Dashboard.cshtml");
    }

    [HttpGet("/baskan"), Roles("bolum_baskani")]
    public IActionResult HeadDashboard()
    {
        var (counts, pending, alerts) = Overview();
        Title("Bölüm Başkanı Paneli");
        ViewBag.Counts = counts;
        ViewBag.Pending = pending;
        ViewBag.Alerts = alerts;
        ViewBag.Recent = C.Db.All("SELECT a.*, s.full_name AS student_name FROM audit_log a LEFT JOIN students s ON s.id = a.student_id ORDER BY a.id DESC LIMIT 15");
        ViewBag.Decisions = C.Db.All("SELECT d.*, s.full_name, s.student_no FROM decisions d JOIN students s ON s.id = d.student_id ORDER BY d.id DESC LIMIT 10");
        return View("~/Views/Coord/HeadDashboard.cshtml");
    }

    [HttpGet("/koordinator/onay"), Roles("koordinator")]
    public IActionResult Queue()
    {
        Title("Onay Bekleyen İşlemler");
        ViewBag.Plan = C.Db.All("SELECT sc.*, c.code, c.name, c.ects, s.full_name, s.student_no, s.program_type FROM student_courses sc JOIN courses c ON c.id = sc.course_id JOIN students s ON s.id = sc.student_id WHERE sc.state = 'onay_bekliyor' ORDER BY sc.id");
        ViewBag.Enrollments = C.Db.All("SELECT e.*, c.code, c.name, c.ects, t.academic_year, t.season, s.full_name, s.student_no FROM enrollments e JOIN courses c ON c.id = e.course_id JOIN terms t ON t.id = e.term_id JOIN students s ON s.id = e.student_id WHERE e.approval_state = 'onay_bekliyor' ORDER BY e.id");
        ViewBag.Grades = C.Db.All("SELECT g.*, c.code, c.name, t.academic_year, t.season, s.full_name, s.student_no, d.state AS doc_state FROM grade_submissions g JOIN enrollments e ON e.id = g.enrollment_id JOIN courses c ON c.id = e.course_id JOIN terms t ON t.id = g.term_id JOIN students s ON s.id = g.student_id JOIN documents d ON d.id = g.transcript_document_id WHERE g.state = 'onay_bekliyor' ORDER BY g.id");
        ViewBag.Documents = C.Db.All("SELECT d.*, s.full_name, s.student_no, t.academic_year, t.season FROM documents d JOIN students s ON s.id = d.student_id LEFT JOIN terms t ON t.id = d.term_id WHERE d.state = 'bekliyor' ORDER BY d.id");
        ViewBag.CourseRequests = C.Db.All("SELECT r.*, s.full_name, s.student_no FROM course_requests r JOIN students s ON s.id = r.student_id WHERE r.state = 'bekliyor' ORDER BY r.id");
        return View("~/Views/Coord/Queue.cshtml");
    }

    [HttpPost("/koordinator/onay/toplu"), Roles("koordinator")]
    public IActionResult BulkApprove() => Act(() =>
    {
        var n = 0;
        var students = new HashSet<long>();
        foreach (var id in InList("plan[]"))
        {
            CourseService.PlanDecide(C, long.Parse(id), true);
            n++;
        }
        foreach (var id in InList("enr[]"))
        {
            students.Add(EnrollmentService.Decide(C, long.Parse(id), true, "", false));
            n++;
        }
        foreach (var id in InList("grade[]"))
        {
            students.Add(EnrollmentService.DecideGrade(C, long.Parse(id), true, "", false));
            n++;
        }
        foreach (var id in InList("doc[]"))
        {
            DocumentService.Decide(C, long.Parse(id), true);
            n++;
        }
        foreach (var sid in students)
        {
            Rules.Snapshot(C, sid, "Toplu onay");
        }
        if (n == 0)
        {
            throw new UserError("Onaylanacak kayıt seçilmedi.");
        }
        C.AddFlash("success", $"{n} kayıt onaylandı.");
    }, null, "/koordinator/onay");

    // ======================= Öğrenci listesi =======================

    [HttpGet("/koordinator/ogrenciler"), Roles("koordinator", "bolum_baskani")]
    public IActionResult Students()
    {
        var q = In("q", 100);
        var type = In("tur", 3);
        var status = In("durum", 20);
        var where = new List<string> { "1=1" };
        var p = new Dapper.DynamicParameters();
        if (q != "")
        {
            where.Add("(LOWER(s.full_name) LIKE @like OR s.student_no LIKE @likeRaw OR LOWER(s.email) LIKE @like)");
            p.Add("like", "%" + q.ToLowerInvariant() + "%");
            p.Add("likeRaw", "%" + q + "%");
        }
        if (L.Program.ContainsKey(type))
        {
            where.Add("s.program_type = @type");
            p.Add("type", type);
        }
        if (L.StudentStatus.ContainsKey(status))
        {
            where.Add("s.status = @status");
            p.Add("status", status);
        }
        ViewBag.Rows = C.Db.All(@"SELECT s.*, t.academic_year, t.season,
                (SELECT summary_level FROM rule_checks rc WHERE rc.student_id = s.id ORDER BY rc.id DESC LIMIT 1) AS level,
                (SELECT COUNT(*) FROM student_courses x WHERE x.student_id = s.id AND x.state = 'onay_bekliyor')
                 + (SELECT COUNT(*) FROM enrollments x WHERE x.student_id = s.id AND x.approval_state = 'onay_bekliyor')
                 + (SELECT COUNT(*) FROM grade_submissions x WHERE x.student_id = s.id AND x.state = 'onay_bekliyor')
                 + (SELECT COUNT(*) FROM documents x WHERE x.student_id = s.id AND x.state = 'bekliyor') AS pending,
                (SELECT gano FROM gano_records g JOIN terms gt ON gt.id = g.term_id WHERE g.student_id = s.id AND g.state = 'gecerli' ORDER BY gt.sort_key DESC, g.id DESC LIMIT 1) AS gano
            FROM students s JOIN terms t ON t.id = s.admission_term_id
            WHERE " + string.Join(" AND ", where) + " ORDER BY s.full_name", p);
        ViewBag.Q = q;
        ViewBag.Type = type;
        ViewBag.Status = status;
        Title("Öğrenciler");
        return View("~/Views/Coord/Students.cshtml");
    }

    [HttpGet("/koordinator/ogrenciler/disa-aktar"), Roles("koordinator", "bolum_baskani")]
    public IActionResult StudentExport()
    {
        var rows = C.Db.All(@"SELECT s.student_no, s.full_name, s.email, s.program_type, t.academic_year, t.season, s.main_faculty, s.main_department, s.status, s.data_source
                              FROM students s JOIN terms t ON t.id = s.admission_term_id ORDER BY s.student_no");
        Audit.Log(C, "student_export", "student", null, null, null, new Data { ["adet"] = rows.Count });
        static string Cell(string v)
        {
            // CSV/Excel formül enjeksiyonuna karşı
            if (v.Length > 0 && "=+-@".Contains(v[0]))
            {
                v = "'" + v;
            }
            return v.Contains(';') || v.Contains('"') || v.Contains('\n') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }
        var sb = new StringBuilder();
        sb.Append('﻿');
        sb.AppendLine("student_no;full_name;email;program_type;admission_term;main_faculty;main_department;status;data_source");
        foreach (var r in rows)
        {
            var line = new[] { r.S("student_no"), r.S("full_name"), r.S("email"), r.S("program_type"), r.S("academic_year") + " " + r.S("season"), r.S("main_faculty"), r.S("main_department"), r.S("status"), r.S("data_source") };
            sb.AppendLine(string.Join(";", line.Select(Cell)));
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", "cap_ydp_ogrenciler_" + Clock.Now().ToString("yyyyMMdd") + ".csv");
    }

    [HttpGet("/koordinator/ogrenci/yeni"), Roles("koordinator")]
    public IActionResult StudentNew()
    {
        Title("Öğrenci Ekle");
        ViewBag.Created = C.Take<List<string[]>>("_created_accounts");
        ViewBag.ImportErrors = C.Take<List<string>>("_import_errors");
        return View("~/Views/Coord/StudentNew.cshtml");
    }

    [HttpPost("/koordinator/ogrenci/yeni"), Roles("koordinator")]
    public IActionResult StudentCreate()
    {
        C.SetJson("_old", FormDict());
        return ActTo(() =>
        {
            var res = StudentService.Upsert(C, new StudentService.StudentInput
            {
                StudentNo = In("student_no", 20),
                FullName = In("full_name", 150),
                Email = In("email", 150),
                ProgramType = In("program_type", 3),
                AdmissionTermId = InLong("admission_term_id") ?? 0,
                MainFaculty = In("main_faculty", 150),
                MainDepartment = In("main_department", 150),
            });
            C.Remove("_old");
            if (!res.Created)
            {
                C.AddFlash("info", "Bu numarada öğrenci zaten vardı; bilgileri güncellendi.");
                return "/koordinator/ogrenci/" + res.Id;
            }
            C.SetJson("_created_accounts", new List<string[]> { new[] { res.Username!, res.Password!, res.Id.ToString() } });
            if (Has("add_protocol") && In("program_type", 3) == "CAP")
            {
                CourseService.AddProtocol(C, res.Id);
            }
            Rules.Snapshot(C, res.Id, "Öğrenci kaydı", false);
            C.AddFlash("success", "Öğrenci eklendi ve hesabı oluşturuldu.");
            return "/koordinator/ogrenci/yeni";
        }, null, "/koordinator/ogrenci/yeni");
    }

    [HttpPost("/koordinator/ogrenci/ice-aktar"), Roles("koordinator")]
    public IActionResult StudentImport() => Act(() =>
    {
        var f = FileIn("csv");
        if (f == null || f.Length > 2 * 1024 * 1024)
        {
            throw new UserError("En fazla 2 MB boyutunda bir CSV dosyası seçin.");
        }
        var text = ReadText(f);
        var res = StudentService.ImportCsv(C, text);
        C.SetJson("_created_accounts", res.Created.Select(x => new[] { x.StudentNo, x.Password, "" }).ToList());
        C.SetJson("_import_errors", res.Errors);
        C.AddFlash("success", $"{res.Created.Count} öğrenci eklendi, {res.Updated} öğrenci güncellendi" + (res.Errors.Count > 0 ? $", {res.Errors.Count} satır hatalı." : "."));
    }, null, "/koordinator/ogrenci/yeni");

    /// <summary>UTF-8 değilse Windows-1254 (Türkçe Excel) olarak okur</summary>
    public static string ReadText(IFormFile f)
    {
        using var ms = new MemoryStream();
        f.CopyTo(ms);
        var bytes = ms.ToArray();
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Türkçe Windows karakterlerini kabaca dönüştür
            var latin = Encoding.Latin1.GetString(bytes);
            return latin.Replace('Ý', 'İ').Replace('ý', 'ı').Replace('Þ', 'Ş').Replace('þ', 'ş').Replace('Ð', 'Ğ').Replace('ð', 'ğ');
        }
    }

    // ======================= Öğrenci kartı =======================

    public static readonly Dictionary<string, string> Tabs = new()
    {
        ["ozet"] = "Özet", ["liste"] = "Ders Listesi", ["dersler"] = "Ders Geçmişi", ["notlar"] = "Not Onayları", ["belgeler"] = "Belgeler",
        ["mesajlar"] = "Mesajlar", ["kartnotu"] = "Koordinatör Notları", ["gano"] = "GANO", ["kararlar"] = "Kararlar", ["kontroller"] = "Kural Kontrolleri", ["gecmis"] = "İşlem Geçmişi",
    };

    private Row? StudentRow(long id) =>
        C.Db.One("SELECT s.*, t.academic_year AS adm_year, t.season AS adm_season FROM students s JOIN terms t ON t.id = s.admission_term_id WHERE s.id = @id", new { id });

    [HttpGet("/koordinator/ogrenci/{id:long}"), Roles("koordinator", "bolum_baskani")]
    public IActionResult Card(long id)
    {
        var s = StudentRow(id);
        if (s == null)
        {
            return Err(404, "Öğrenci bulunamadı.");
        }
        var tab = In("tab", 20);
        if (!Tabs.ContainsKey(tab))
        {
            tab = "ozet";
        }
        var rs = Rules.ActiveSet(C, s.S("program_type"));
        Title(s.S("full_name") + " — Öğrenci Kartı");
        ViewBag.S = s;
        ViewBag.Tab = tab;
        ViewBag.Ro = !C.IsCoordinator;
        ViewBag.Rs = rs;
        ViewBag.Progress = Progress.Compute(C, id, rs.Params);
        ViewBag.Account = C.Db.One("SELECT id, username, is_active, last_login_at, must_change_password, locked_until FROM users WHERE student_id = @id", new { id });
        ViewBag.UnreadMsgs = C.Db.Count("SELECT COUNT(*) FROM messages WHERE student_id = @id AND sender_role = 'ogrenci' AND read_at IS NULL", new { id });
        var sid = new { id };
        switch (tab)
        {
            case "ozet":
                ViewBag.Eval = Rules.Evaluate(C, id, rs);
                ViewBag.Gano = C.Db.One("SELECT g.*, t.academic_year, t.season FROM gano_records g JOIN terms t ON t.id = g.term_id WHERE g.student_id = @id AND g.state = 'gecerli' ORDER BY t.sort_key DESC, g.id DESC LIMIT 1", sid);
                ViewBag.Terms = Terms.All(C);
                break;
            case "liste":
                ViewBag.Items = C.Db.All("SELECT sc.*, c.code, c.name, c.ects, c.semester, c.category, u.full_name AS creator FROM student_courses sc JOIN courses c ON c.id = sc.course_id LEFT JOIN users u ON u.id = sc.created_by WHERE sc.student_id = @id ORDER BY CASE sc.state WHEN 'onay_bekliyor' THEN 0 WHEN 'onaylandi' THEN 1 ELSE 2 END, c.semester, c.code", sid);
                ViewBag.Catalog = C.Db.All("SELECT id, code, name, ects, semester FROM courses WHERE is_active = 1 AND program_scope IN ('HER_IKISI', @type) ORDER BY code", new { type = s.S("program_type") });
                break;
            case "dersler":
                ViewBag.Catalog = C.Db.All("SELECT id, code, name, ects FROM courses ORDER BY code");
                ViewBag.Scale = rs.Params.Scale.Keys.ToList();
                ViewBag.Terms = Terms.All(C);
                break;
            case "notlar":
                ViewBag.Subs = C.Db.All(@"SELECT g.*, c.code, c.name, t.academic_year, t.season, d.title AS doc_title, d.state AS doc_state, e.attempt_no
                    FROM grade_submissions g JOIN enrollments e ON e.id = g.enrollment_id JOIN courses c ON c.id = e.course_id JOIN terms t ON t.id = g.term_id JOIN documents d ON d.id = g.transcript_document_id
                    WHERE g.student_id = @id ORDER BY CASE g.state WHEN 'onay_bekliyor' THEN 0 ELSE 1 END, t.sort_key DESC, c.code", sid);
                break;
            case "belgeler":
                ViewBag.Docs = C.Db.All("SELECT d.*, t.academic_year, t.season, u.full_name AS uploader FROM documents d LEFT JOIN terms t ON t.id = d.term_id JOIN users u ON u.id = d.uploaded_by WHERE d.student_id = @id ORDER BY d.id DESC", sid);
                ViewBag.Requests = C.Db.All("SELECT r.*, d.title AS doc_title FROM document_requests r LEFT JOIN documents d ON d.id = r.fulfilled_document_id WHERE r.student_id = @id ORDER BY r.id DESC", sid);
                ViewBag.Terms = Terms.All(C);
                break;
            case "mesajlar":
                if (C.IsCoordinator)
                {
                    MessageService.MarkRead(C, id, "koordinator");
                }
                ViewBag.Thread = MessageService.Thread(C, id);
                break;
            case "kartnotu":
                ViewBag.Notes = C.Db.All("SELECT n.*, u.full_name AS author FROM coordinator_notes n JOIN users u ON u.id = n.author_id WHERE n.student_id = @id ORDER BY n.id DESC", sid);
                break;
            case "gano":
                ViewBag.Ganos = C.Db.All("SELECT g.*, t.academic_year, t.season, u.full_name AS author FROM gano_records g JOIN terms t ON t.id = g.term_id JOIN users u ON u.id = g.entered_by WHERE g.student_id = @id ORDER BY t.sort_key DESC, g.id DESC", sid);
                ViewBag.Terms = Terms.All(C);
                break;
            case "kararlar":
                ViewBag.Decisions = C.Db.All("SELECT d.*, t.academic_year, t.season, u.full_name AS author, doc.title AS doc_title FROM decisions d LEFT JOIN terms t ON t.id = d.term_id JOIN users u ON u.id = d.decided_by LEFT JOIN documents doc ON doc.id = d.document_id WHERE d.student_id = @id ORDER BY d.id DESC", sid);
                ViewBag.Docs = C.Db.All("SELECT id, title, created_at FROM documents WHERE student_id = @id AND state <> 'iptal' ORDER BY id DESC", sid);
                ViewBag.Eval = Rules.Evaluate(C, id, rs);
                ViewBag.Terms = Terms.All(C);
                break;
            case "kontroller":
                ViewBag.Checks = C.Db.All("SELECT rc.*, r.version_no, r.title AS rs_title, u.full_name AS runner FROM rule_checks rc JOIN rule_sets r ON r.id = rc.rule_set_id LEFT JOIN users u ON u.id = rc.run_by WHERE rc.student_id = @id ORDER BY rc.id DESC LIMIT 50", sid);
                break;
            case "gecmis":
                ViewBag.Logs = C.Db.All("SELECT * FROM audit_log WHERE student_id = @id ORDER BY id DESC LIMIT 300", sid);
                break;
        }
        return View("~/Views/Coord/Card.cshtml");
    }

    private static string To(long sid, string tab) => "/koordinator/ogrenci/" + sid + "?tab=" + tab;

    [HttpPost("/koordinator/ogrenci/{id:long}/guncelle"), Roles("koordinator")]
    public IActionResult StudentUpdate(long id)
    {
        var s = StudentRow(id);
        if (s == null)
        {
            return Err(404, "Öğrenci bulunamadı.");
        }
        return Act(() =>
        {
            StudentService.Upsert(C, new StudentService.StudentInput
            {
                StudentNo = s.S("student_no"),
                FullName = In("full_name", 150),
                Email = In("email", 150),
                ProgramType = s.S("program_type"),
                AdmissionTermId = InLong("admission_term_id") ?? 0,
                MainFaculty = In("main_faculty", 150),
                MainDepartment = In("main_department", 150),
            });
            C.Db.Exec("UPDATE users SET full_name = @n, email = @e WHERE student_id = @id", new { n = In("full_name", 150), e = In("email", 150).ToLowerInvariant(), id });
        }, "Öğrenci bilgileri güncellendi.", To(id, "ozet"));
    }

    [HttpPost("/koordinator/ogrenci/{id:long}/sifre"), Roles("koordinator")]
    public IActionResult StudentPassword(long id) => Act(() =>
    {
        var uid = C.Db.Count("SELECT id FROM users WHERE student_id = @id", new { id });
        var pw = AdminService.ResetPassword(C, uid);
        C.AddFlash("success", "Öğrencinin geçici şifresi: " + pw + " — İlk girişte değiştirmesi istenecek. Bu şifre yalnızca bir kez gösterilir.");
    }, null, To(id, "ozet"));

    // ---- Ders listesi ----
    [HttpPost("/koordinator/ogrenci/{id:long}/liste/ekle"), Roles("koordinator")]
    public IActionResult PlanAdd(long id) => Act(() => CourseService.PlanAdd(C, id, InLong("course_id") ?? 0, In("reason", 500)), "Ders listeye eklendi.", To(id, "liste"));

    [HttpPost("/koordinator/ogrenci/{id:long}/liste/protokol"), Roles("koordinator")]
    public IActionResult PlanProtocol(long id) => Act(() =>
    {
        var type = C.Db.Str("SELECT program_type FROM students WHERE id = @id", new { id });
        if (type != "CAP")
        {
            throw new UserError("Protokol şablonu yalnızca ÇAP öğrencileri içindir.");
        }
        var n = CourseService.AddProtocol(C, id);
        C.AddFlash("success", $"Protokol şablonundan {n} ders eklendi.");
    }, null, To(id, "liste"));

    [HttpPost("/koordinator/liste/{pid:long}/karar"), Roles("koordinator")]
    public IActionResult PlanDecide(long pid) => Act(() => CourseService.PlanDecide(C, pid, In("karar") == "onay", In("reason", 500)), "İşlem kaydedildi.");

    [HttpPost("/koordinator/liste/{pid:long}/cikar"), Roles("koordinator")]
    public IActionResult PlanRemove(long pid) => Act(() => CourseService.PlanRemove(C, pid, In("reason", 500)), "Ders listeden çıkarıldı (geçmiş kayıtlar korunur).");

    // ---- Ders geçmişi ----
    [HttpPost("/koordinator/ogrenci/{id:long}/ders/ekle"), Roles("koordinator")]
    public IActionResult EnrollAdd(long id) => Act(() => EnrollmentService.CoordinatorAdd(C, id, InLong("course_id") ?? 0, InLong("term_id") ?? 0, In("status", 12),
        In("grade", 3) == "" ? null : In("grade", 3), In("legacy") == "1", In("note", 500)), "Ders kaydı eklendi.", To(id, "dersler"));

    [HttpPost("/koordinator/ders/{eid:long}/karar"), Roles("koordinator")]
    public IActionResult EnrollDecide(long eid) => Act(() => EnrollmentService.Decide(C, eid, In("karar") == "onay", In("reason", 500)), "İşlem kaydedildi.");

    [HttpPost("/koordinator/ders/{eid:long}/duzelt"), Roles("koordinator")]
    public IActionResult EnrollCorrect(long eid) => Act(() => EnrollmentService.Correct(C, eid, InLong("term_id") ?? 0, In("status", 12), In("grade", 3) == "" ? null : In("grade", 3), In("reason", 500)),
        "Kayıt düzeltildi; eski ve yeni değerler işlem kaydına yazıldı.");

    [HttpPost("/koordinator/ders/{eid:long}/iptal"), Roles("koordinator")]
    public IActionResult EnrollCancel(long eid) => Act(() => EnrollmentService.Cancel(C, eid, In("reason", 500)), "Kayıt iptal edildi (silinmedi).");

    [HttpPost("/koordinator/not/{gid:long}/karar"), Roles("koordinator")]
    public IActionResult GradeDecide(long gid) => Act(() => EnrollmentService.DecideGrade(C, gid, In("karar") == "onay", In("reason", 500)), "İşlem kaydedildi.");

    // ---- Belgeler ----
    [HttpPost("/koordinator/ogrenci/{id:long}/belge"), Roles("koordinator")]
    public IActionResult DocUpload(long id) => Act(() =>
    {
        var f = FileGuard.Check(C, FileIn("file"));
        DocumentService.Store(C, id, In("doc_type", 40), InLong("term_id"), In("title", 200), f, null, InLong("supersedes_id"));
    }, "Belge yüklendi.", To(id, "belgeler"));

    [HttpPost("/koordinator/belge/{did:long}/karar"), Roles("koordinator")]
    public IActionResult DocDecide(long did) => Act(() => DocumentService.Decide(C, did, In("karar") == "onay", In("reason", 500)), "İşlem kaydedildi.");

    [HttpPost("/koordinator/belge/{did:long}/iptal"), Roles("koordinator")]
    public IActionResult DocCancel(long did) => Act(() => DocumentService.Cancel(C, did, In("reason", 500)), "Belge iptal olarak işaretlendi (arşivde kalır).");

    [HttpPost("/koordinator/ogrenci/{id:long}/istek"), Roles("koordinator")]
    public IActionResult RequestCreate(long id) => Act(() => DocumentService.CreateRequest(C, id, In("doc_type", 40), In("description", 1000), In("due_date", 10)),
        "Belge isteği öğrenciye iletildi.", To(id, "belgeler"));

    [HttpPost("/koordinator/istek/{rid:long}/iptal"), Roles("koordinator")]
    public IActionResult RequestCancel(long rid) => Act(() => DocumentService.CancelRequest(C, rid, In("reason", 500)), "İstek iptal edildi.");

    // ---- Mesaj, not, GANO, karar ----
    [HttpPost("/koordinator/ogrenci/{id:long}/mesaj"), Roles("koordinator")]
    public IActionResult Message(long id) => Act(() =>
    {
        var file = FileIn("file");
        MessageService.Send(C, id, In("body", 5000), file == null ? null : FileGuard.Check(C, file));
    }, "Mesaj gönderildi.", To(id, "mesajlar"));

    [HttpPost("/koordinator/ogrenci/{id:long}/not"), Roles("koordinator")]
    public IActionResult NoteAdd(long id) => Act(() => StudentService.AddNote(C, id, In("body", 5000)), "Not eklendi.", To(id, "kartnotu"));

    [HttpPost("/koordinator/kart-notu/{nid:long}/iptal"), Roles("koordinator")]
    public IActionResult NoteCancel(long nid) => ActTo(() => To(StudentService.CancelNote(C, nid, In("reason", 500)), "kartnotu"), "Not iptal edildi.");

    [HttpPost("/koordinator/ogrenci/{id:long}/gano"), Roles("koordinator")]
    public IActionResult GanoAdd(long id) => Act(() => StudentService.AddGano(C, id, InLong("term_id") ?? 0, InDec("gano"), In("source", 100), In("note", 500)),
        "GANO kaydedildi ve kural kontrolü yapıldı.", To(id, "gano"));

    [HttpPost("/koordinator/gano/{gid:long}/iptal"), Roles("koordinator")]
    public IActionResult GanoCancel(long gid) => ActTo(() => To(StudentService.CancelGano(C, gid, In("reason", 500)), "gano"), "GANO kaydı iptal edildi.");

    [HttpPost("/koordinator/ogrenci/{id:long}/karar"), Roles("koordinator")]
    public IActionResult Decision(long id) => Act(() => StudentService.Decide(C, id, In("decision_type", 20), InLong("term_id"), In("decision_date", 10), In("reference_no", 100),
        In("reason", 1000), InLong("document_id")), "Karar kaydedildi.", To(id, "kararlar"));

    [HttpPost("/koordinator/ogrenci/{id:long}/kontrol"), Roles("koordinator")]
    public IActionResult RuleCheck(long id) => Act(() =>
    {
        var r = Rules.Snapshot(C, id, "Koordinatör manuel kontrolü");
        Audit.Log(C, "rule_check", "student", id, id, null, new Data { ["sonuc"] = r.Level });
        C.AddFlash("success", "Kural kontrolü kaydedildi. Sonuç: " + L.Level[r.Level]);
    }, null, To(id, "kontroller"));
}
