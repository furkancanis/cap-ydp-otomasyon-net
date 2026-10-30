namespace CapYdp.Services;

/// <summary>
/// Kimlik doğrulama.
/// - Parolalar PBKDF2-SHA256 ile saklanır
/// - Hesap bazında kilitleme: 5 hatalı denemede 15 dk
/// - IP bazında sınırlama: 15 dk içinde 20 başarısız deneme
/// - Girişte oturum kimliği yenilenir (session fixation koruması)
/// </summary>
public static class AuthService
{
    public const int MaxUserFails = 5;
    public const int LockMinutes = 15;
    public const int MaxIpFails = 20;

    public static Actor? LoadActor(IAppCtx c, long uid)
    {
        var u = c.Db.One("SELECT id, username, role, full_name, email, student_id, is_active, must_change_password FROM users WHERE id = @uid", new { uid });
        if (u == null || !u.B("is_active"))
        {
            return null;
        }
        return new Actor(u.L("id"), u.S("username"), u.S("role"), u.S("full_name"), u.Sn("email"), u.Ln("student_id"), u.B("must_change_password"));
    }

    public static string? Attempt(RequestCtx c, string username, string password)
    {
        var ip = c.Ip;
        var since = Clock.Now().AddMinutes(-LockMinutes);
        var ipFails = c.Db.Count("SELECT COUNT(*) FROM login_attempts WHERE ip_address = @ip AND success = 0 AND created_at > @since", new { ip, since });
        if (ipFails >= MaxIpFails)
        {
            return $"Çok fazla başarısız deneme yapıldı. Lütfen {LockMinutes} dakika sonra tekrar deneyin.";
        }

        username = username.Trim().ToLowerInvariant();
        var u = c.Db.One("SELECT * FROM users WHERE username = @username", new { username });
        // kullanıcı yoksa da aynı sürede doğrulama yapılır (kullanıcı adı tahminine karşı)
        var valid = Passwords.Verify(password, u?.S("password_hash") ?? Passwords.Dummy);
        const string generic = "Kullanıcı adı veya şifre hatalı.";
        if (u == null)
        {
            Record(c, ip, username, false);
            return generic;
        }
        var actor = new Actor(u.L("id"), u.S("username"), u.S("role"), u.S("full_name"), u.Sn("email"), u.Ln("student_id"), false);
        if (u.Dt("locked_until") is DateTime lu && lu > Clock.Now())
        {
            Record(c, ip, username, false);
            return $"Hesabınız art arda hatalı denemeler nedeniyle geçici olarak kilitlendi. {LockMinutes} dakika sonra tekrar deneyin.";
        }
        if (!u.B("is_active"))
        {
            Record(c, ip, username, false);
            return "Hesabınız pasif durumdadır. Koordinatörle iletişime geçin.";
        }
        if (!valid)
        {
            var fails = u.I("failed_attempts") + 1;
            var upd = new Data { ["failed_attempts"] = fails };
            if (fails >= MaxUserFails)
            {
                upd["locked_until"] = Clock.Now().AddMinutes(LockMinutes);
                upd["failed_attempts"] = 0;
            }
            c.Db.Update("users", upd, "id = @id", new { id = u.L("id") });
            Record(c, ip, username, false);
            Audit.Log(c, "login_failed", "user", u.L("id"), u.Ln("student_id"), actor: actor);
            return generic;
        }

        c.Db.Update("users", new Data { ["failed_attempts"] = 0, ["locked_until"] = null, ["last_login_at"] = Clock.Now() }, "id = @id", new { id = u.L("id") });
        Record(c, ip, username, true);

        c.ClearAll();
        c.Regenerate();
        c.SetUser(u.L("id"));
        Audit.Log(c, "login", "user", u.L("id"), u.Ln("student_id"), actor: actor);
        return null;
    }

    public static void Logout(RequestCtx c)
    {
        if (c.User != null)
        {
            Audit.Log(c, "logout", "user", c.User.Id);
        }
        c.DestroySession();
    }

    public static string Home(string? role) => role switch
    {
        "koordinator" => "/koordinator",
        "bolum_baskani" => "/baskan",
        "ogrenci" => "/ogrenci",
        _ => "/giris",
    };

    private static void Record(IAppCtx c, string ip, string username, bool ok)
    {
        c.Db.Insert("login_attempts", new Data
        {
            ["ip_address"] = ip,
            ["username"] = username.Length > 60 ? username[..60] : username,
            ["success"] = ok ? 1 : 0,
            ["created_at"] = Clock.Now(),
        });
        if (Random.Shared.Next(50) == 0)
        {
            c.Db.Exec("DELETE FROM login_attempts WHERE created_at < @t", new { t = Clock.Now().AddDays(-30) });
        }
    }
}
