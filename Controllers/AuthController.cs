using System.Text.RegularExpressions;
using CapYdp.Web;
using Microsoft.AspNetCore.Mvc;

namespace CapYdp.Controllers;

public sealed class AuthController : AppController
{
    [HttpGet("/")]
    public IActionResult Home() => Go(C.User != null ? AuthService.Home(C.Role) : "/giris");

    [HttpGet("/giris")]
    public IActionResult LoginForm()
    {
        if (C.User != null)
        {
            return Go(AuthService.Home(C.Role));
        }
        Title("Giriş");
        return View("~/Views/Auth/Login.cshtml");
    }

    [HttpPost("/giris")]
    public async Task<IActionResult> Login()
    {
        var username = In("username", 60);
        var err = AuthService.Attempt(C, username, Request.Form["password"].ToString());
        if (err != null)
        {
            await Task.Delay(Random.Shared.Next(150, 350));
            C.AddFlash("error", err);
            C.SetJson("_old", new Dictionary<string, string> { ["username"] = username });
            return Go("/giris");
        }
        C.Remove("_old");
        var to = AuthService.Home(C.Role);
        var intended = C.Get("_intended");
        C.Remove("_intended");
        if (intended != null && Regex.IsMatch(intended, "^/[a-z0-9/\\-]*$") && intended.StartsWith(to))
        {
            to = intended;
        }
        return Go(to);
    }

    [HttpPost("/cikis"), Roles]
    public IActionResult Logout()
    {
        AuthService.Logout(C);
        C.AddFlash("success", "Güvenli şekilde çıkış yaptınız.");
        return Go("/giris");
    }

    [HttpGet("/sifre"), Roles]
    public IActionResult PasswordForm()
    {
        Title("Şifre Değiştir");
        return View("~/Views/Auth/Password.cshtml");
    }

    [HttpPost("/sifre"), Roles]
    public IActionResult PasswordUpdate() => ActTo(() =>
    {
        AdminService.ChangeOwnPassword(C, Request.Form["current"].ToString(), Request.Form["new"].ToString(), Request.Form["confirm"].ToString());
        C.Regenerate();
        C.ReloadUser();
        return AuthService.Home(C.Role);
    }, "Şifreniz güncellendi.", "/sifre");

    [HttpGet("/bildirimler"), Roles]
    public IActionResult Notifications()
    {
        Title("Bildirimler");
        ViewBag.Items = C.Db.All("SELECT * FROM notifications WHERE user_id = @uid ORDER BY id DESC LIMIT 100", new { uid = C.User!.Id });
        return View("~/Views/Shared/Notifications.cshtml");
    }

    [HttpPost("/bildirimler/okundu"), Roles]
    public IActionResult NotificationsRead()
    {
        C.Db.Exec("UPDATE notifications SET read_at = @t WHERE user_id = @uid AND read_at IS NULL", new { t = Clock.Now(), uid = C.User!.Id });
        var go = In("go", 255);
        if (go != "" && Regex.IsMatch(go, @"^/[A-Za-z0-9/\-?=&_]*$") && !go.StartsWith("//"))
        {
            return Go(go);
        }
        return Go("/bildirimler");
    }

    /// <summary>Belgeyi yetki kontrolüyle sunar (öğrenci yalnızca kendi belgesini görebilir)</summary>
    [HttpGet("/belge/{id:long}"), Roles]
    public IActionResult Document(long id)
    {
        var d = C.Db.One("SELECT * FROM documents WHERE id = @id", new { id });
        if (d == null || !DocumentService.CanView(C, d))
        {
            // var olmayan ve yetkisiz belge aynı yanıtı alır (kimlik tahminine karşı)
            return Err(404, "Belge bulunamadı.");
        }
        var data = DocumentService.Content(C, id);
        Audit.Log(C, "document_view", "document", id, d.L("student_id"));
        var mime = d.S("mime_type");
        var inline = !Has("indir") && mime is "application/pdf" or "image/png" or "image/jpeg";
        var fname = Regex.Replace(d.S("original_name"), "[^A-Za-z0-9._-]", "_");
        Response.Headers.Remove("Content-Security-Policy");
        if (mime != "application/pdf")
        {
            // görüntüler kum havuzunda (sandbox) sunulur; PDF'ler yüklemede betik içeriğine karşı taranmıştır
            Response.Headers["Content-Security-Policy"] = "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox";
        }
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["Content-Disposition"] = (inline ? "inline" : "attachment") + "; filename=\"" + fname + "\"; filename*=UTF-8''" + Uri.EscapeDataString(d.S("original_name"));
        return File(data, mime);
    }

    [HttpGet("/saglik")]
    public IActionResult Health()
    {
        return Json(new { durum = "ok", veritabani = "pgsql", surum = "1.0.0", platform = ".NET " + Environment.Version });
    }

    [Route("/hata/{code:int}"), SkipCsrf]
    public IActionResult Error(int code)
    {
        var msg = code switch
        {
            404 => "Aradığınız sayfa bulunamadı.",
            405 => "Bu adres için izin verilmeyen istek yöntemi.",
            500 => "Beklenmeyen bir hata oluştu. Lütfen daha sonra tekrar deneyin.",
            _ => "",
        };
        return ErrorResult.Make(code is >= 400 and < 600 ? code : 500, msg);
    }
}
