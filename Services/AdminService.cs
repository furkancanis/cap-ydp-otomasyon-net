using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CapYdp.Services;

/// <summary>Dönemler, kullanıcılar, yönerge kural sürümleri ve sistem ayarları</summary>
public static class AdminService
{
    // ---------- Dönemler ----------

    public static long CreateTerm(IAppCtx c, string year, string season)
    {
        var m = Regex.Match(year ?? "", @"^(\d{4})-(\d{4})$");
        if (!m.Success || int.Parse(m.Groups[2].Value) != int.Parse(m.Groups[1].Value) + 1)
        {
            throw new UserError("Akademik yıl 2025-2026 biçiminde olmalıdır.");
        }
        if (!Terms.Seasons.ContainsKey(season ?? ""))
        {
            throw new UserError("Dönem Güz, Bahar veya Yaz olmalıdır.");
        }
        if (c.Db.Scalar("SELECT id FROM terms WHERE academic_year = @year AND season = @season", new { year, season }) != null)
        {
            throw new UserError("Bu dönem zaten tanımlı.");
        }
        var id = c.Db.Insert("terms", new Data
        {
            ["academic_year"] = year, ["season"] = season, ["sort_key"] = Terms.SortKey(year!, season!),
            ["is_current"] = 0, ["is_active"] = 1, ["created_at"] = Clock.Now(),
        });
        Audit.Log(c, "term_create", "term", id, null, null, new Data { ["donem"] = year + " " + season });
        return id;
    }

    public static void SetCurrentTerm(IAppCtx c, long id)
    {
        var t = Terms.Find(c, id) ?? throw new UserError("Dönem bulunamadı.");
        var old = c.Db.One("SELECT academic_year, season FROM terms WHERE is_current = 1");
        c.Db.Tx(() =>
        {
            c.Db.Exec("UPDATE terms SET is_current = 0 WHERE is_current = 1");
            c.Db.Exec("UPDATE terms SET is_current = 1 WHERE id = @id", new { id });
        });
        Terms.ResetCache(c);
        Audit.Log(c, "term_update", "term", id, null, new Data { ["aktif_donem"] = old == null ? null : Terms.Label(old) }, new Data { ["aktif_donem"] = Terms.Label(t) });
    }

    // ---------- Kullanıcılar ----------

    public static string CreateStaff(IAppCtx c, string username, string fullName, string email, string role)
    {
        username = (username ?? "").Trim().ToLowerInvariant();
        email = (email ?? "").Trim();
        if (!Regex.IsMatch(username, @"^[a-z0-9._-]{3,60}$"))
        {
            throw new UserError("Kullanıcı adı 3-60 karakter; küçük harf, rakam, nokta, tire içerebilir.");
        }
        if (role is not ("koordinator" or "bolum_baskani"))
        {
            throw new UserError("Rol geçersiz.");
        }
        if ((fullName ?? "").Trim().Length < 3)
        {
            throw new UserError("Ad soyad girilmelidir.");
        }
        if (email != "" && !Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            throw new UserError("E-posta geçersiz.");
        }
        if (c.Db.Scalar("SELECT id FROM users WHERE username = @username", new { username }) != null)
        {
            throw new UserError("Bu kullanıcı adı kullanılıyor.");
        }
        var pw = Passwords.Random();
        var id = c.Db.Insert("users", new Data
        {
            ["username"] = username, ["password_hash"] = Passwords.Hash(pw), ["role"] = role,
            ["full_name"] = fullName!.Trim(), ["email"] = email == "" ? null : email, ["is_active"] = 1, ["must_change_password"] = 1, ["created_at"] = Clock.Now(),
        });
        Audit.Log(c, "user_create", "user", id, null, null, new Data { ["username"] = username, ["role"] = role });
        return pw;
    }

    public static string ResetPassword(IAppCtx c, long userId)
    {
        var u = c.Db.One("SELECT * FROM users WHERE id = @id", new { id = userId }) ?? throw new UserError("Kullanıcı bulunamadı.");
        var pw = Passwords.Random();
        c.Db.Update("users", new Data { ["password_hash"] = Passwords.Hash(pw), ["must_change_password"] = 1, ["failed_attempts"] = 0, ["locked_until"] = null }, "id = @id", new { id = userId });
        Audit.Log(c, "password_reset", "user", userId, u.Ln("student_id"), null, new Data { ["username"] = u.S("username") });
        return pw;
    }

    public static void ToggleActive(IAppCtx c, long userId)
    {
        var u = c.Db.One("SELECT * FROM users WHERE id = @id", new { id = userId }) ?? throw new UserError("Kullanıcı bulunamadı.");
        if (userId == c.User!.Id)
        {
            throw new UserError("Kendi hesabınızı pasifleştiremezsiniz.");
        }
        var nw = u.B("is_active") ? 0 : 1;
        c.Db.Update("users", new Data { ["is_active"] = nw }, "id = @id", new { id = userId });
        Audit.Log(c, "user_update", "user", userId, u.Ln("student_id"), new Data { ["is_active"] = u.I("is_active") }, new Data { ["is_active"] = nw });
    }

    public static void ChangeOwnPassword(IAppCtx c, string current, string nw, string confirm)
    {
        var u = c.Db.One("SELECT * FROM users WHERE id = @id", new { id = c.User!.Id })!;
        if (!Passwords.Verify(current ?? "", u.S("password_hash")))
        {
            throw new UserError("Mevcut şifre hatalı.");
        }
        if (nw != confirm)
        {
            throw new UserError("Yeni şifreler uyuşmuyor.");
        }
        var err = Passwords.PolicyError(nw ?? "");
        if (err != null)
        {
            throw new UserError(err);
        }
        if (Passwords.Verify(nw!, u.S("password_hash")))
        {
            throw new UserError("Yeni şifre eskisiyle aynı olamaz.");
        }
        c.Db.Update("users", new Data { ["password_hash"] = Passwords.Hash(nw!), ["must_change_password"] = 0, ["password_changed_at"] = Clock.Now() }, "id = @id", new { id = u.L("id") });
        Audit.Log(c, "password_change", "user", u.L("id"));
    }

    // ---------- Yönerge kural sürümleri ----------

    /// <summary>
    /// Yeni kural sürümü oluşturur. Mevcut sürümler DEĞİŞTİRİLMEZ; yeni sürüm yürürlüğe alınınca
    /// eskisi pasif olur ama saklanır. Geçmiş kontroller (rule_checks) kendi sürüm kimliğini taşır.
    /// </summary>
    public static long CreateRuleSet(IAppCtx c, string type, string title, string validFrom, IDictionary<string, string> raw, string note, bool activate)
    {
        if (!Rules.ParamDefs.ContainsKey(type ?? ""))
        {
            throw new UserError("Program türü geçersiz.");
        }
        if ((title ?? "").Trim().Length < 3)
        {
            throw new UserError("Sürüm başlığı girilmelidir (ör. \"Senato 20/05/2026 - 2026-20\").");
        }
        if (!DateTime.TryParseExact(validFrom ?? "", "yyyy-MM-dd", null, DateTimeStyles.None, out var vf))
        {
            throw new UserError("Yürürlük tarihi geçersiz.");
        }
        var obj = new JsonObject();
        foreach (var (key, label, kind, _) in Rules.ParamDefs[type!])
        {
            var v = (raw.TryGetValue(key, out var rv) ? rv : "").Trim();
            if (v == "")
            {
                throw new UserError($"\"{label}\" boş bırakılamaz.");
            }
            switch (kind)
            {
                case "number":
                    var dv = CourseService.ParseDec(v);
                    if (dv == null)
                    {
                        throw new UserError($"\"{label}\" sayısal olmalıdır.");
                    }
                    obj[key] = dv.Value;
                    break;
                case "int":
                    if (!Regex.IsMatch(v, @"^\d+$"))
                    {
                        throw new UserError($"\"{label}\" tam sayı olmalıdır.");
                    }
                    obj[key] = int.Parse(v);
                    break;
                case "json":
                    JsonNode? j;
                    try
                    {
                        j = JsonNode.Parse(v);
                    }
                    catch
                    {
                        j = null;
                    }
                    if (j is not (JsonObject or JsonArray))
                    {
                        throw new UserError($"\"{label}\" geçerli JSON olmalıdır.");
                    }
                    obj[key] = j;
                    break;
                default:
                    obj[key] = v.Length > 200 ? v[..200] : v;
                    break;
            }
        }
        var ver = 1 + c.Db.Count("SELECT COALESCE(MAX(version_no), 0) FROM rule_sets WHERE program_type = @type", new { type });
        var paramsJson = obj.ToJsonString(Json.Options);
        var id = c.Db.Tx(() =>
        {
            if (activate)
            {
                c.Db.Exec("UPDATE rule_sets SET is_active = 0 WHERE program_type = @type AND is_active = 1", new { type });
            }
            return c.Db.Insert("rule_sets", new Data
            {
                ["program_type"] = type, ["version_no"] = (int)ver, ["title"] = title!.Trim(),
                ["params"] = paramsJson, ["valid_from"] = vf,
                ["is_active"] = activate ? 1 : 0, ["note"] = string.IsNullOrWhiteSpace(note) ? null : note, ["created_by"] = c.User?.Id, ["created_at"] = Clock.Now(),
            });
        });
        Rules.ClearCache(c);
        var logged = new Data { ["program"] = type, ["surum"] = ver, ["baslik"] = title, ["yururlukte"] = activate ? "evet" : "hayır", ["parametreler"] = paramsJson };
        Audit.Log(c, "ruleset_create", "rule_set", id, null, null, logged);
        return id;
    }

    public static void ActivateRuleSet(IAppCtx c, long id)
    {
        var r = c.Db.One("SELECT * FROM rule_sets WHERE id = @id", new { id }) ?? throw new UserError("Sürüm bulunamadı.");
        var old = c.Db.One("SELECT id, version_no FROM rule_sets WHERE program_type = @type AND is_active = 1", new { type = r.S("program_type") });
        c.Db.Tx(() =>
        {
            c.Db.Exec("UPDATE rule_sets SET is_active = 0 WHERE program_type = @type AND is_active = 1", new { type = r.S("program_type") });
            c.Db.Exec("UPDATE rule_sets SET is_active = 1 WHERE id = @id", new { id });
        });
        Rules.ClearCache(c);
        Audit.Log(c, "ruleset_activate", "rule_set", id, null, new Data { ["aktif_surum"] = old?.I("version_no") }, new Data { ["aktif_surum"] = r.I("version_no"), ["program"] = r.S("program_type") });
    }

    // ---------- Ayarlar ----------

    public static readonly List<(string Key, string Label, string Regex)> SettingDefs = new()
    {
        ("max_upload_mb", "En büyük dosya boyutu (MB)", @"^\d{1,2}$"),
        ("allowed_extensions", "İzin verilen uzantılar (virgülle; yalnızca pdf,jpg,jpeg,png desteklenir)", @"^(pdf|jpg|jpeg|png)(,(pdf|jpg|jpeg|png))*$"),
        ("student_email_domain", "Öğrenci e-posta alan adı (boş = kontrol yok)", @"^([a-z0-9.-]+\.[a-z]{2,})?$"),
        ("department_name", "Bölüm adı", @"^.{3,150}$"),
        ("coordinator_title", "Koordinatör unvanı/adı (bilgi amaçlı)", @"^.{0,150}$"),
    };

    public static void UpdateSettings(IAppCtx c, IDictionary<string, string> input)
    {
        foreach (var (key, label, re) in SettingDefs)
        {
            if (!input.TryGetValue(key, out var raw))
            {
                continue;
            }
            var v = (raw ?? "").Trim();
            if (key == "allowed_extensions")
            {
                v = v.Replace(" ", "").ToLowerInvariant();
            }
            if (!Regex.IsMatch(v, re))
            {
                throw new UserError($"\"{label}\" geçersiz.");
            }
            var old = c.Db.Str("SELECT setting_value FROM settings WHERE setting_key = @k", new { k = key });
            if (old == v)
            {
                continue;
            }
            if (old == null)
            {
                c.Db.InsertNoId("settings", new Data { ["setting_key"] = key, ["setting_value"] = v, ["description"] = label, ["updated_by"] = c.User!.Id, ["updated_at"] = Clock.Now() });
            }
            else
            {
                c.Db.Update("settings", new Data { ["setting_value"] = v, ["updated_by"] = c.User!.Id, ["updated_at"] = Clock.Now() }, "setting_key = @k", new { k = key });
            }
            Audit.Log(c, "setting_update", "setting", null, null, new Data { [key] = old }, new Data { [key] = v });
        }
        c.Cache.Remove("settings");
    }
}
