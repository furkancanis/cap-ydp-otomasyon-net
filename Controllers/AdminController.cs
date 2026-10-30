using System.Text.RegularExpressions;
using CapYdp.Web;
using Microsoft.AspNetCore.Mvc;

namespace CapYdp.Controllers;

public sealed class AdminController : AppController
{
    // ---- Yönerge kuralları ----
    [HttpGet("/koordinator/kurallar"), Roles("koordinator", "bolum_baskani")]
    public IActionResult RulesPage()
    {
        ViewBag.Sets = C.Db.All("SELECT r.*, u.full_name AS author, (SELECT COUNT(*) FROM rule_checks rc WHERE rc.rule_set_id = r.id) AS used FROM rule_sets r LEFT JOIN users u ON u.id = r.created_by ORDER BY r.program_type, r.version_no DESC");
        ViewBag.Active = new Dictionary<string, RuleSet> { ["CAP"] = Rules.ActiveSet(C, "CAP"), ["YDP"] = Rules.ActiveSet(C, "YDP") };
        ViewBag.Edit = In("yeni", 3);
        Title("Yönerge Kuralları");
        return View("~/Views/Coord/Rules.cshtml");
    }

    [HttpPost("/koordinator/kurallar"), Roles("koordinator")]
    public IActionResult RuleCreate() => Act(() => AdminService.CreateRuleSet(C, In("program_type", 3), In("title", 200), In("valid_from", 10),
            FormPrefix("p"), In("note", 1000), In("activate") == "1"),
        "Yeni kural sürümü kaydedildi. Önceki sürümler ve geçmiş kontroller korunur.", "/koordinator/kurallar");

    [HttpPost("/koordinator/kurallar/{id:long}/aktif"), Roles("koordinator")]
    public IActionResult RuleActivate(long id) => Act(() => AdminService.ActivateRuleSet(C, id), "Kural sürümü yürürlüğe alındı.", "/koordinator/kurallar");

    [HttpPost("/koordinator/kurallar/toplu-kontrol"), Roles("koordinator")]
    public IActionResult RuleRunAll() => Act(() =>
    {
        var n = 0;
        foreach (var s in C.Db.All("SELECT id FROM students WHERE status IN ('aktif','donduruldu')"))
        {
            Rules.Snapshot(C, s.L("id"), "Toplu kural kontrolü");
            n++;
        }
        Audit.Log(C, "rule_check", "student", null, null, null, new Data { ["toplu"] = n });
        C.AddFlash("success", $"{n} öğrenci için kural kontrolü yapıldı ve kaydedildi.");
    }, null, "/koordinator");

    // ---- Kullanıcılar ----
    [HttpGet("/koordinator/kullanicilar"), Roles("koordinator")]
    public IActionResult Users()
    {
        ViewBag.Rows = C.Db.All("SELECT u.*, s.student_no FROM users u LEFT JOIN students s ON s.id = u.student_id ORDER BY CASE u.role WHEN 'koordinator' THEN 0 WHEN 'bolum_baskani' THEN 1 ELSE 2 END, u.full_name");
        Title("Kullanıcı Hesapları");
        return View("~/Views/Coord/Users.cshtml");
    }

    [HttpPost("/koordinator/kullanicilar"), Roles("koordinator")]
    public IActionResult UserCreate() => Act(() =>
    {
        var pw = AdminService.CreateStaff(C, In("username", 60), In("full_name", 150), In("email", 150), In("role", 20));
        C.AddFlash("success", "Hesap oluşturuldu. Geçici şifre: " + pw + " (yalnızca bir kez gösterilir; ilk girişte değiştirilecek).");
    }, null, "/koordinator/kullanicilar");

    [HttpPost("/koordinator/kullanicilar/{id:long}/sifre"), Roles("koordinator")]
    public IActionResult UserReset(long id) => Act(() =>
    {
        var pw = AdminService.ResetPassword(C, id);
        C.AddFlash("success", "Yeni geçici şifre: " + pw + " (yalnızca bir kez gösterilir).");
    }, null, "/koordinator/kullanicilar");

    [HttpPost("/koordinator/kullanicilar/{id:long}/durum"), Roles("koordinator")]
    public IActionResult UserToggle(long id) => Act(() => AdminService.ToggleActive(C, id), "Hesap durumu değiştirildi.", "/koordinator/kullanicilar");

    // ---- İşlem kaydı ----
    [HttpGet("/koordinator/islem-kaydi"), Roles("koordinator", "bolum_baskani")]
    public IActionResult AuditPage()
    {
        var where = new List<string> { "1=1" };
        var p = new Dapper.DynamicParameters();
        var q = In("q", 100);
        var act = In("islem", 60);
        var from = In("bas", 10);
        var to = In("bit", 10);
        if (q != "")
        {
            where.Add("(LOWER(a.username) LIKE @like OR LOWER(COALESCE(s.full_name, '')) LIKE @like OR COALESCE(s.student_no, '') LIKE @likeRaw)");
            p.Add("like", "%" + q.ToLowerInvariant() + "%");
            p.Add("likeRaw", "%" + q + "%");
        }
        if (Audit.ActionLabels.ContainsKey(act))
        {
            where.Add("a.action = @act");
            p.Add("act", act);
        }
        if (DateTime.TryParseExact(from, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var fd))
        {
            where.Add("a.created_at >= @fd");
            p.Add("fd", fd);
        }
        if (DateTime.TryParseExact(to, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var td))
        {
            where.Add("a.created_at < @td");
            p.Add("td", td.AddDays(1));
        }
        var page = Math.Max(1, (int)(InLong("sayfa") ?? 1));
        const int per = 50;
        var w = string.Join(" AND ", where);
        ViewBag.Total = C.Db.Count($"SELECT COUNT(*) FROM audit_log a LEFT JOIN students s ON s.id = a.student_id WHERE {w}", p);
        ViewBag.Rows = C.Db.All($"SELECT a.*, s.full_name AS student_name, s.student_no FROM audit_log a LEFT JOIN students s ON s.id = a.student_id WHERE {w} ORDER BY a.id DESC LIMIT {per} OFFSET {(page - 1) * per}", p);
        ViewBag.Page = page;
        ViewBag.Per = per;
        ViewBag.Q = q;
        ViewBag.Act = act;
        ViewBag.From = from;
        ViewBag.To = to;
        Title("İşlem Kaydı (Denetim)");
        return View("~/Views/Coord/Audit.cshtml");
    }

    // ---- Ayarlar ----
    [HttpGet("/koordinator/ayarlar"), Roles("koordinator")]
    public IActionResult SettingsPage()
    {
        var vals = new Dictionary<string, string>();
        foreach (var r in C.Db.All("SELECT * FROM settings"))
        {
            vals[r.S("setting_key")] = r.S("setting_value");
        }
        ViewBag.Vals = vals;
        Title("Sistem Ayarları");
        return View("~/Views/Coord/Settings.cshtml");
    }

    [HttpPost("/koordinator/ayarlar"), Roles("koordinator")]
    public IActionResult SettingsUpdate() => Act(() => AdminService.UpdateSettings(C, FormDict()), "Ayarlar kaydedildi.", "/koordinator/ayarlar");
}
