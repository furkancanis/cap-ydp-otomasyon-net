using CapYdp.Web;
using Microsoft.AspNetCore.Mvc;

namespace CapYdp.Controllers;

/// <summary>Öğrenci paneli. Öğrenci YALNIZCA kendi kaydını görür; öğrenci kimliği her zaman oturumdan alınır.</summary>
[Roles("ogrenci")]
public sealed class StudentController : AppController
{
    private long Sid => C.User?.StudentId ?? 0;

    private Row Me() => C.Db.One("SELECT s.*, t.academic_year AS adm_year, t.season AS adm_season FROM students s JOIN terms t ON t.id = s.admission_term_id WHERE s.id = @id", new { id = Sid })
                        ?? throw new InvalidOperationException("Hesabınız bir öğrenci kaydına bağlı değil.");

    [HttpGet("/ogrenci")]
    public IActionResult Dashboard()
    {
        var s = Me();
        var rs = Rules.ActiveSet(C, s.S("program_type"));
        var current = Terms.Current(C);
        ViewBag.S = s;
        ViewBag.Eval = Rules.Evaluate(C, Sid, rs);
        ViewBag.Requests = C.Db.All("SELECT * FROM document_requests WHERE student_id = @sid AND state IN ('bekliyor','reddedildi') ORDER BY id DESC", new { sid = Sid });
        ViewBag.Unread = C.Db.Count("SELECT COUNT(*) FROM messages WHERE student_id = @sid AND sender_role <> 'ogrenci' AND read_at IS NULL", new { sid = Sid });
        ViewBag.Progress = Progress.Compute(C, Sid, rs.Params);
        ViewBag.Current = current;
        ViewBag.CurrentCourses = current == null ? new List<Row>() : C.Db.All("SELECT e.*, c.code, c.name, c.ects FROM enrollments e JOIN courses c ON c.id = e.course_id WHERE e.student_id = @sid AND e.term_id = @tid AND e.approval_state IN ('onay_bekliyor','onaylandi') ORDER BY c.code",
            new { sid = Sid, tid = current.L("id") });
        Title("Öğrenci Paneli");
        return View("~/Views/Student/Dashboard.cshtml");
    }

    [HttpGet("/ogrenci/dersler")]
    public IActionResult Courses()
    {
        var s = Me();
        var rs = Rules.ActiveSet(C, s.S("program_type"));
        var cur = Terms.Current(C);
        ViewBag.S = s;
        ViewBag.Progress = Progress.Compute(C, Sid, rs.Params);
        ViewBag.PlanOther = C.Db.All("SELECT sc.*, c.code, c.name, c.ects, c.semester, c.category FROM student_courses sc JOIN courses c ON c.id = sc.course_id WHERE sc.student_id = @sid AND sc.state <> 'onaylandi' ORDER BY sc.id DESC", new { sid = Sid });
        ViewBag.Catalog = C.Db.All("SELECT id, code, name, ects, semester, category FROM courses WHERE is_active = 1 AND program_scope IN ('HER_IKISI', @type) ORDER BY code", new { type = s.S("program_type") });
        ViewBag.OpenTerms = C.Db.All("SELECT * FROM terms WHERE is_active = 1 AND sort_key >= @k ORDER BY sort_key LIMIT 3", new { k = cur?.I("sort_key") ?? 0 });
        ViewBag.Requests = C.Db.All("SELECT * FROM course_requests WHERE student_id = @sid ORDER BY id DESC", new { sid = Sid });
        Title("Derslerim");
        return View("~/Views/Student/Courses.cshtml");
    }

    [HttpPost("/ogrenci/liste/ekle")]
    public IActionResult PlanAdd() => Act(() =>
    {
        var code = In("course_code", 120).ToUpperInvariant();
        // "3301156 - Ders adı" biçiminde gelirse yalnız kodu al
        code = code.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var cid = C.Db.Scalar("SELECT id FROM courses WHERE code = @code AND is_active = 1", new { code });
        if (cid == null)
        {
            throw new UserError("Ders kataloğunda \"" + code + "\" kodlu aktif ders bulunamadı. Katalogda olmayan dersler için aşağıdan koordinatöre talep gönderin.");
        }
        CourseService.PlanAdd(C, Sid, Convert.ToInt64(cid));
    }, "Ders listenize eklendi; koordinatör onayı bekleniyor.", "/ogrenci/dersler");

    [HttpPost("/ogrenci/liste/{id:long}/geri-cek")]
    public IActionResult PlanWithdraw(long id) => Act(() => CourseService.PlanWithdraw(C, id, Sid), "Talep geri çekildi.", "/ogrenci/dersler");

    [HttpPost("/ogrenci/donem-dersi")]
    public IActionResult EnrollAdd() => Act(() => EnrollmentService.StudentAdd(C, Sid, InLong("course_id") ?? 0, InLong("term_id") ?? 0),
        "Dönem dersi girildi; koordinatör onayı bekleniyor.", "/ogrenci/dersler");

    [HttpPost("/ogrenci/donem-dersi/{id:long}/geri-cek")]
    public IActionResult EnrollWithdraw(long id) => Act(() => EnrollmentService.Withdraw(C, id, Sid), "Talep geri çekildi.", "/ogrenci/dersler");

    [HttpPost("/ogrenci/ders-talebi")]
    public IActionResult CourseRequest() => Act(() => CourseService.RequestCourse(C, Sid, In("course_code", 20), In("course_name", 200), In("explanation", 1000)),
        "Talebiniz koordinatöre iletildi.", "/ogrenci/dersler");

    [HttpGet("/ogrenci/notlar")]
    public IActionResult Grades()
    {
        var s = Me();
        var terms = C.Db.All("SELECT DISTINCT t.* FROM enrollments e JOIN terms t ON t.id = e.term_id WHERE e.student_id = @sid AND e.approval_state = 'onaylandi' AND e.status = 'alindi' ORDER BY t.sort_key DESC", new { sid = Sid });
        var termId = InLong("donem") ?? (terms.Count > 0 ? terms[0].L("id") : 0);
        ViewBag.S = s;
        ViewBag.TermsList = terms;
        ViewBag.TermId = termId;
        ViewBag.Rows = termId > 0 ? EnrollmentService.GradeableFor(C, Sid, termId) : new List<Row>();
        ViewBag.Transcript = termId > 0 ? C.Db.One("SELECT * FROM documents WHERE student_id = @sid AND doc_type = 'cap_ydp_transkript' AND term_id = @tid AND state IN ('bekliyor','onaylandi') ORDER BY id DESC LIMIT 1", new { sid = Sid, tid = termId }) : null;
        ViewBag.History = C.Db.All("SELECT g.*, c.code, c.name, t.academic_year, t.season FROM grade_submissions g JOIN enrollments e ON e.id = g.enrollment_id JOIN courses c ON c.id = e.course_id JOIN terms t ON t.id = g.term_id WHERE g.student_id = @sid ORDER BY g.id DESC", new { sid = Sid });
        ViewBag.Scale = Rules.ActiveSet(C, s.S("program_type")).Params.Scale.Keys.ToList();
        Title("Dönem Sonu Not Girişi");
        return View("~/Views/Student/Grades.cshtml");
    }

    [HttpPost("/ogrenci/notlar")]
    public IActionResult GradesSubmit()
    {
        var termId = InLong("term_id") ?? 0;
        return Act(() =>
        {
            var grades = new Dictionary<long, string>();
            foreach (var kv in FormDict())
            {
                if (kv.Key.StartsWith("grade[") && kv.Key.EndsWith("]") && long.TryParse(kv.Key[6..^1], out var eid))
                {
                    grades[eid] = kv.Value;
                }
            }
            var n = EnrollmentService.SubmitGrades(C, Sid, termId, grades);
            C.AddFlash("success", $"{n} dersin notu koordinatör onayına gönderildi.");
        }, null, "/ogrenci/notlar?donem=" + termId);
    }

    [HttpGet("/ogrenci/belgeler")]
    public IActionResult Documents()
    {
        ViewBag.S = Me();
        ViewBag.Docs = C.Db.All("SELECT d.*, t.academic_year, t.season FROM documents d LEFT JOIN terms t ON t.id = d.term_id WHERE d.student_id = @sid ORDER BY d.id DESC", new { sid = Sid });
        ViewBag.Requests = C.Db.All("SELECT * FROM document_requests WHERE student_id = @sid ORDER BY CASE state WHEN 'bekliyor' THEN 0 WHEN 'reddedildi' THEN 1 ELSE 2 END, id DESC", new { sid = Sid });
        ViewBag.Terms = Terms.All(C);
        Title("Belgelerim");
        return View("~/Views/Student/Documents.cshtml");
    }

    [HttpPost("/ogrenci/belgeler")]
    public IActionResult DocUpload()
    {
        var to = In("ret") == "notlar" ? "/ogrenci/notlar?donem=" + (InLong("term_id") ?? 0) : "/ogrenci/belgeler";
        return Act(() =>
        {
            var f = FileGuard.Check(C, FileIn("file"));
            var type = In("doc_type", 40);
            if (type is "yonetim_kurulu_karari" or "mesaj_eki")
            {
                throw new UserError("Bu belge türünü yalnızca koordinatör yükleyebilir.");
            }
            var req = InLong("request_id");
            if (req != null)
            {
                var r = C.Db.One("SELECT doc_type FROM document_requests WHERE id = @id AND student_id = @sid", new { id = req, sid = Sid }) ?? throw new UserError("Belge isteği bulunamadı.");
                type = r.S("doc_type");
            }
            DocumentService.Store(C, Sid, type, InLong("term_id"), In("title", 200), f, req, InLong("supersedes_id"));
        }, "Belge yüklendi; koordinatör incelemesi bekleniyor.", to);
    }

    [HttpGet("/ogrenci/mesajlar")]
    public IActionResult Messages()
    {
        ViewBag.S = Me();
        MessageService.MarkRead(C, Sid, "ogrenci");
        ViewBag.Thread = MessageService.Thread(C, Sid);
        Title("Mesajlar");
        return View("~/Views/Student/Messages.cshtml");
    }

    [HttpPost("/ogrenci/mesajlar")]
    public IActionResult MessageSend() => Act(() =>
    {
        var file = FileIn("file");
        MessageService.Send(C, Sid, In("body", 5000), file == null ? null : FileGuard.Check(C, file));
    }, "Mesajınız gönderildi.", "/ogrenci/mesajlar");
}
