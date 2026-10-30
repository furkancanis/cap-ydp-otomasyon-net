using CapYdp.Web;
using Microsoft.AspNetCore.Mvc;

namespace CapYdp.Controllers;

public sealed class CatalogController : AppController
{
    [HttpGet("/koordinator/katalog"), Roles("koordinator", "bolum_baskani")]
    public IActionResult Index()
    {
        var q = In("q", 100);
        var where = "1=1";
        object? p = null;
        if (q != "")
        {
            where = "(LOWER(code) LIKE @like OR LOWER(name) LIKE @like)";
            p = new { like = "%" + q.ToLowerInvariant() + "%" };
        }
        ViewBag.Rows = C.Db.All($"SELECT c.*, (SELECT COUNT(*) FROM student_courses sc WHERE sc.course_id = c.id AND sc.state = 'onaylandi') AS used FROM courses c WHERE {where} ORDER BY c.is_active DESC, c.semester, c.code", p);
        ViewBag.Q = q;
        ViewBag.ImportErrors = C.Take<List<string>>("_cimport_errors");
        Title("Ders Kataloğu");
        return View("~/Views/Coord/Catalog.cshtml");
    }

    [HttpPost("/koordinator/katalog"), Roles("koordinator")]
    public IActionResult Create() => Act(() => CourseService.CreateCourse(C, FormDict()), "Ders kataloğa eklendi.", "/koordinator/katalog");

    [HttpGet("/koordinator/katalog/{id:long}"), Roles("koordinator")]
    public IActionResult Edit(long id)
    {
        var c = C.Db.One("SELECT * FROM courses WHERE id = @id", new { id });
        if (c == null)
        {
            return Err(404, "Ders bulunamadı.");
        }
        ViewBag.Course = c;
        ViewBag.Logs = C.Db.All("SELECT * FROM audit_log WHERE entity_type = 'course' AND entity_id = @id ORDER BY id DESC", new { id });
        Title("Ders Düzenle — " + c.S("code"));
        return View("~/Views/Coord/CourseEdit.cshtml");
    }

    [HttpPost("/koordinator/katalog/{id:long}"), Roles("koordinator")]
    public IActionResult Update(long id) => Act(() => CourseService.UpdateCourse(C, id, FormDict()), "Ders güncellendi.", "/koordinator/katalog/" + id);

    [HttpPost("/koordinator/katalog/ice-aktar"), Roles("koordinator")]
    public IActionResult Import() => Act(() =>
    {
        var f = FileIn("csv");
        if (f == null || f.Length > 2 * 1024 * 1024)
        {
            throw new UserError("En fazla 2 MB boyutunda bir CSV dosyası seçin.");
        }
        var (created, updated, errors) = CourseService.ImportCsv(C, CoordinatorController.ReadText(f));
        C.SetJson("_cimport_errors", errors);
        C.AddFlash("success", $"{created} ders eklendi, {updated} ders güncellendi.");
    }, null, "/koordinator/katalog");

    [HttpGet("/koordinator/ders-talepleri"), Roles("koordinator", "bolum_baskani")]
    public IActionResult Requests()
    {
        ViewBag.Rows = C.Db.All("SELECT r.*, s.full_name, s.student_no, c.code AS created_code FROM course_requests r JOIN students s ON s.id = r.student_id LEFT JOIN courses c ON c.id = r.created_course_id ORDER BY CASE r.state WHEN 'bekliyor' THEN 0 ELSE 1 END, r.id DESC");
        Title("Katalog Dışı Ders Talepleri");
        return View("~/Views/Coord/CourseRequests.cshtml");
    }

    [HttpPost("/koordinator/ders-talepleri/{id:long}"), Roles("koordinator")]
    public IActionResult RequestDecide(long id) => Act(() =>
    {
        var accept = In("karar") == "kabul";
        CourseService.DecideRequest(C, id, accept, In("response", 500), accept ? FormDict() : null);
    }, "Talep sonuçlandırıldı.", "/koordinator/ders-talepleri");

    [HttpGet("/koordinator/donemler"), Roles("koordinator", "bolum_baskani")]
    public IActionResult TermsPage()
    {
        ViewBag.Rows = C.Db.All("SELECT t.*, (SELECT COUNT(*) FROM enrollments e WHERE e.term_id = t.id AND e.approval_state = 'onaylandi') AS n FROM terms t ORDER BY t.sort_key DESC");
        Title("Akademik Dönemler");
        return View("~/Views/Coord/Terms.cshtml");
    }

    [HttpPost("/koordinator/donemler"), Roles("koordinator")]
    public IActionResult TermCreate() => Act(() => AdminService.CreateTerm(C, In("academic_year", 9), In("season", 10)), "Dönem eklendi.", "/koordinator/donemler");

    [HttpPost("/koordinator/donemler/{id:long}/aktif"), Roles("koordinator")]
    public IActionResult TermCurrent(long id) => Act(() => AdminService.SetCurrentTerm(C, id), "Aktif dönem değiştirildi.", "/koordinator/donemler");
}
