namespace CapYdp.Services;

/// <summary>Kullanıcıya gösterilecek doğrulama / iş kuralı hatası</summary>
public sealed class UserError : Exception
{
    public UserError(string message) : base(message)
    {
    }
}

/// <summary>
/// İşlem kaydı (denetim): kim, ne zaman, neyi, eski ve yeni değeriyle ne yaptı.
/// audit_log tablosu veritabanı tetikleyicisi ile değiştirilemez ve silinemez.
/// </summary>
public static class Audit
{
    public static void Log(IAppCtx c, string action, string entityType, long? entityId = null, long? studentId = null,
        IDictionary<string, object?>? old = null, IDictionary<string, object?>? nw = null, string? reason = null, Actor? actor = null)
    {
        var u = actor ?? c.User;
        c.Db.Insert("audit_log", new Data
        {
            ["created_at"] = Clock.Now(),
            ["user_id"] = u?.Id,
            ["username"] = u?.Username ?? "sistem",
            ["user_role"] = u?.Role ?? "sistem",
            ["action"] = action,
            ["entity_type"] = entityType,
            ["entity_id"] = entityId,
            ["student_id"] = studentId,
            ["old_values"] = old == null ? null : Json.Encode(Clean(old)),
            ["new_values"] = nw == null ? null : Json.Encode(Clean(nw)),
            ["reason"] = reason == null ? null : (reason.Length > 1000 ? reason[..1000] : reason),
            ["ip_address"] = c.Ip,
            ["user_agent"] = c.UserAgent,
        });
    }

    private static Dictionary<string, object?> Clean(IDictionary<string, object?> a)
    {
        var r = new Dictionary<string, object?>();
        foreach (var kv in a)
        {
            if (kv.Key is "password_hash" or "content")
            {
                continue;
            }
            r[kv.Key] = kv.Value switch
            {
                DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
                byte[] => "(ikili veri)",
                _ => kv.Value
            };
        }
        return r;
    }

    public static string Norm(object? v)
    {
        return v switch
        {
            null => "",
            decimal m => m.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture),
            double d => ((decimal)d).ToString("0.############", System.Globalization.CultureInfo.InvariantCulture),
            float f => ((decimal)f).ToString("0.############", System.Globalization.CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
            bool b => b ? "1" : "0",
            _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? ""
        };
    }

    /// <summary>Yalnızca değişen alanları döndürür (eski, yeni)</summary>
    public static (Data Old, Data New) Diff(Row old, IDictionary<string, object?> nw)
    {
        var o = new Data();
        var n = new Data();
        foreach (var kv in nw)
        {
            var ov = old[kv.Key];
            if (Norm(ov) != Norm(kv.Value))
            {
                o[kv.Key] = ov;
                n[kv.Key] = kv.Value;
            }
        }
        return (o, n);
    }

    public static readonly Dictionary<string, string> ActionLabels = new()
    {
        ["login"] = "Giriş yaptı", ["login_failed"] = "Başarısız giriş", ["logout"] = "Çıkış yaptı",
        ["password_change"] = "Şifre değiştirdi", ["password_reset"] = "Şifre sıfırladı",
        ["student_create"] = "Öğrenci ekledi", ["student_update"] = "Öğrenci bilgisi güncelledi", ["student_import"] = "Öğrenci içe aktardı",
        ["student_export"] = "Öğrenci listesini dışa aktardı",
        ["user_create"] = "Kullanıcı oluşturdu", ["user_update"] = "Kullanıcı güncelledi",
        ["plan_add"] = "Ders listesine ders ekledi", ["plan_approve"] = "Ders listesi kaydını onayladı",
        ["plan_reject"] = "Ders listesi kaydını reddetti", ["plan_remove"] = "Ders listesinden ders çıkardı", ["plan_withdraw"] = "Ders listesi talebini geri çekti",
        ["enrollment_add"] = "Dönem dersi ekledi", ["enrollment_approve"] = "Dönem dersini onayladı", ["enrollment_reject"] = "Dönem dersini reddetti",
        ["enrollment_correct"] = "Ders kaydını düzeltti", ["enrollment_cancel"] = "Ders kaydını iptal etti", ["enrollment_withdraw"] = "Dönem dersi talebini geri çekti",
        ["grade_submit"] = "Not girdi (onaya gönderdi)", ["grade_approve"] = "Notu onayladı", ["grade_reject"] = "Notu reddetti",
        ["document_upload"] = "Belge yükledi", ["document_approve"] = "Belgeyi onayladı", ["document_reject"] = "Belgeyi reddetti", ["document_cancel"] = "Belgeyi iptal etti",
        ["document_view"] = "Belgeyi görüntüledi",
        ["request_create"] = "Ek belge istedi", ["request_update"] = "Belge isteğini güncelledi",
        ["message_send"] = "Mesaj gönderdi",
        ["note_add"] = "Koordinatör notu ekledi", ["note_cancel"] = "Koordinatör notunu iptal etti",
        ["gano_add"] = "GANO girdi", ["gano_cancel"] = "GANO kaydını iptal etti",
        ["course_create"] = "Kataloğa ders ekledi", ["course_update"] = "Katalog dersini güncelledi", ["course_import"] = "Katalog içe aktardı",
        ["course_request"] = "Katalog dışı ders talebi gönderdi", ["course_request_decide"] = "Ders talebini sonuçlandırdı",
        ["term_create"] = "Dönem ekledi", ["term_update"] = "Dönemi güncelledi",
        ["ruleset_create"] = "Yönerge kural sürümü oluşturdu", ["ruleset_activate"] = "Kural sürümünü yürürlüğe aldı",
        ["rule_check"] = "Kural kontrolü çalıştırdı", ["decision"] = "Karar verdi", ["decision_cancel"] = "Kararı iptal etti",
        ["setting_update"] = "Sistem ayarını güncelledi",
    };
}

/// <summary>Uygulama içi bildirimler</summary>
public static class Notify
{
    public static void User(IAppCtx c, long userId, string message, string? link = null)
    {
        c.Db.Insert("notifications", new Data
        {
            ["user_id"] = userId,
            ["message"] = message.Length > 500 ? message[..500] : message,
            ["link"] = link,
            ["created_at"] = Clock.Now(),
        });
    }

    public static void Student(IAppCtx c, long studentId, string message, string? link = null)
    {
        foreach (var u in c.Db.All("SELECT id FROM users WHERE student_id = @sid AND role = 'ogrenci' AND is_active = 1", new { sid = studentId }))
        {
            User(c, u.L("id"), message, link ?? "/ogrenci");
        }
    }

    public static void Coordinators(IAppCtx c, string message, string? link = null)
    {
        foreach (var u in c.Db.All("SELECT id FROM users WHERE role = 'koordinator' AND is_active = 1"))
        {
            User(c, u.L("id"), message, link);
        }
    }

    public static long UnreadCount(IAppCtx c, long userId) =>
        c.Db.Count("SELECT COUNT(*) FROM notifications WHERE user_id = @uid AND read_at IS NULL", new { uid = userId });
}

/// <summary>Akademik dönemler</summary>
public static class Terms
{
    public static readonly Dictionary<string, int> Seasons = new() { ["Güz"] = 1, ["Bahar"] = 2, ["Yaz"] = 3 };

    public static Row? Current(IAppCtx c)
    {
        if (!c.Cache.TryGetValue("term_current", out var v))
        {
            v = c.Db.One("SELECT * FROM terms WHERE is_current = 1 ORDER BY sort_key DESC LIMIT 1");
            c.Cache["term_current"] = v;
        }
        return (Row?)v;
    }

    public static void ResetCache(IAppCtx c) => c.Cache.Remove("term_current");

    public static List<Row> All(IAppCtx c, bool activeOnly = true) =>
        c.Db.All("SELECT * FROM terms" + (activeOnly ? " WHERE is_active = 1" : "") + " ORDER BY sort_key DESC");

    public static string Label(Row? t) => t == null ? "—" : t.S("academic_year") + " " + t.S("season");

    public static int SortKey(string year, string season) => int.Parse(year[..4]) * 10 + (Seasons.TryGetValue(season, out var s) ? s : 1);

    public static Row? Find(IAppCtx c, long id) => c.Db.One("SELECT * FROM terms WHERE id = @id", new { id });

    /// <summary>"2025-2026 Güz" metninden dönem bulur / oluşturur (içe aktarma için)</summary>
    public static Row? Resolve(IAppCtx c, string text, bool create = false)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text ?? "", @"^\s*(\d{4})\s*-\s*(\d{4})\s+(\S+)\s*$");
        if (!m.Success)
        {
            return null;
        }
        var low = H.TrLower(m.Groups[3].Value);
        var season = low switch
        {
            "güz" or "guz" => "Güz",
            "bahar" => "Bahar",
            "yaz" => "Yaz",
            _ => ""
        };
        if (season == "")
        {
            return null;
        }
        var year = m.Groups[1].Value + "-" + m.Groups[2].Value;
        var t = c.Db.One("SELECT * FROM terms WHERE academic_year = @year AND season = @season", new { year, season });
        if (t == null && create)
        {
            var id = c.Db.Insert("terms", new Data
            {
                ["academic_year"] = year, ["season"] = season, ["sort_key"] = SortKey(year, season),
                ["is_current"] = 0, ["is_active"] = 1, ["created_at"] = Clock.Now(),
            });
            t = Find(c, id);
        }
        return t;
    }
}

/// <summary>Anahtar/değer sistem ayarları</summary>
public static class Settings
{
    public static string Get(IAppCtx c, string key, string def = "")
    {
        if (!c.Cache.TryGetValue("settings", out var v))
        {
            var d = new Dictionary<string, string>();
            try
            {
                foreach (var r in c.Db.All("SELECT setting_key, setting_value FROM settings"))
                {
                    d[r.S("setting_key")] = r.S("setting_value");
                }
            }
            catch
            {
                // kurulum öncesi
            }
            v = d;
            c.Cache["settings"] = v;
        }
        var map = (Dictionary<string, string>)v!;
        return map.TryGetValue(key, out var s) ? s : def;
    }
}
