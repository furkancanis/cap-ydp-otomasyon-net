using System.Globalization;
using System.Text.RegularExpressions;

namespace CapYdp.Services;

/// <summary>Ders kataloğu, öğrenci ders listesi (protokol) ve katalog dışı ders talepleri</summary>
public static class CourseService
{
    private static string G(IDictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? (v ?? "").Trim() : "";

    public static decimal? ParseDec(string? s)
    {
        s = (s ?? "").Trim().Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    // ================= Katalog =================

    public static Data ValidateCourse(IDictionary<string, string> d)
    {
        var code = Regex.Replace(G(d, "code"), @"\s+", "").ToUpperInvariant();
        var name = G(d, "name");
        if (!Regex.IsMatch(code, @"^[A-Z0-9\-]{2,20}$"))
        {
            throw new UserError("Ders kodu 2-20 karakter (harf, rakam, tire) olmalıdır.");
        }
        if (name.Length < 2)
        {
            throw new UserError("Ders adı girilmelidir.");
        }
        var ects = ParseDec(G(d, "ects")) ?? 0;
        var credit = ParseDec(G(d, "credit") == "" ? "0" : G(d, "credit")) ?? -1;
        if (ects <= 0 || ects > 40)
        {
            throw new UserError("AKTS 0-40 arasında olmalıdır.");
        }
        if (credit < 0 || credit > 40)
        {
            throw new UserError("Kredi 0-40 arasında olmalıdır.");
        }
        int? sem = null;
        if (G(d, "semester") != "")
        {
            if (!int.TryParse(G(d, "semester"), out var sv) || sv < 1 || sv > 12)
            {
                throw new UserError("Yarıyıl 1-12 arasında olmalıdır.");
            }
            sem = sv;
        }
        var cat = G(d, "category") == "" ? "zorunlu" : G(d, "category");
        var scope = G(d, "program_scope") == "" ? "HER_IKISI" : G(d, "program_scope");
        if (!L.Category.ContainsKey(cat) || !L.Scope.ContainsKey(scope))
        {
            throw new UserError("Kategori veya kapsam geçersiz.");
        }
        var desc = G(d, "description");
        return new Data
        {
            ["code"] = code,
            ["name"] = name.Length > 200 ? name[..200] : name,
            ["ects"] = ects,
            ["credit"] = credit,
            ["semester"] = sem,
            ["category"] = cat,
            ["program_scope"] = scope,
            ["in_cap_protocol"] = G(d, "in_cap_protocol") is "1" or "on" or "true" ? 1 : 0,
            ["description"] = desc == "" ? null : (desc.Length > 500 ? desc[..500] : desc),
        };
    }

    public static long CreateCourse(IAppCtx c, IDictionary<string, string> d)
    {
        var row = ValidateCourse(d);
        if (c.Db.Scalar("SELECT id FROM courses WHERE code = @code", new { code = (string)row["code"] }) != null)
        {
            throw new UserError("Bu ders kodu katalogda zaten var: " + row["code"]);
        }
        var ins = new Data(row) { ["is_active"] = 1, ["created_at"] = Clock.Now(), ["updated_at"] = Clock.Now() };
        var id = c.Db.Insert("courses", ins);
        Audit.Log(c, "course_create", "course", id, null, null, row);
        return id;
    }

    public static void UpdateCourse(IAppCtx c, long id, IDictionary<string, string> d)
    {
        var old = c.Db.One("SELECT * FROM courses WHERE id = @id", new { id }) ?? throw new UserError("Ders bulunamadı.");
        var row = ValidateCourse(d);
        if ((string)row["code"] != old.S("code") && c.Db.Scalar("SELECT id FROM courses WHERE code = @code AND id <> @id", new { code = (string)row["code"], id }) != null)
        {
            throw new UserError("Bu ders kodu başka bir derste kullanılıyor.");
        }
        row["is_active"] = G(d, "is_active") is "1" or "on" or "true" ? 1 : 0;
        var (o, n) = Audit.Diff(old, row);
        if (n.Count == 0)
        {
            return;
        }
        var upd = new Data(n) { ["updated_at"] = Clock.Now() };
        c.Db.Update("courses", upd, "id = @id", new { id });
        var reason = G(d, "reason");
        Audit.Log(c, "course_update", "course", id, null, o, n, reason == "" ? null : reason);
    }

    /// <summary>CSV: code;name;credit;ects;semester;category;program_scope;in_cap_protocol</summary>
    public static (int Created, int Updated, List<string> Errors) ImportCsv(IAppCtx c, string csv)
    {
        var (header, rows) = StudentService.ParseCsv(csv);
        if (!header.Contains("code"))
        {
            throw new UserError("CSV başlığında code sütunu bulunmalıdır.");
        }
        int created = 0, updated = 0;
        var errors = new List<string>();
        foreach (var (lineNo, r) in rows)
        {
            try
            {
                if (r == null)
                {
                    throw new UserError("sütun sayısı hatalı");
                }
                var id = c.Db.Scalar("SELECT id FROM courses WHERE code = @code", new { code = G(r, "code").ToUpperInvariant() });
                if (id != null)
                {
                    r["is_active"] = "1";
                    UpdateCourse(c, Convert.ToInt64(id), r);
                    updated++;
                }
                else
                {
                    CreateCourse(c, r);
                    created++;
                }
            }
            catch (UserError e)
            {
                errors.Add($"Satır {lineNo}: {e.Message}");
            }
        }
        Audit.Log(c, "course_import", "course", null, null, null, new Data { ["created"] = created, ["updated"] = updated, ["errors"] = errors.Count });
        return (created, updated, errors);
    }

    // ================= Öğrenci ders listesi =================

    /// <summary>Ders listesine ekleme. Öğrenci eklerse "onay bekliyor", koordinatör eklerse doğrudan onaylı.</summary>
    public static long PlanAdd(IAppCtx c, long studentId, long courseId, string reason = "", bool legacy = false)
    {
        var u = c.User!;
        var st = c.Db.One("SELECT * FROM students WHERE id = @id", new { id = studentId });
        var co = c.Db.One("SELECT * FROM courses WHERE id = @id", new { id = courseId });
        if (st == null || co == null)
        {
            throw new UserError("Ders veya öğrenci bulunamadı.");
        }
        if (!co.B("is_active"))
        {
            throw new UserError("Bu ders katalogda pasif durumda.");
        }
        if (co.S("program_scope") != "HER_IKISI" && co.S("program_scope") != st.S("program_type"))
        {
            throw new UserError("Bu ders öğrencinin programı (" + L.ProgramShort[st.S("program_type")] + ") için uygun değil.");
        }
        if (c.Db.Scalar("SELECT id FROM student_courses WHERE student_id = @sid AND course_id = @cid AND state IN ('onay_bekliyor','onaylandi')", new { sid = studentId, cid = courseId }) != null)
        {
            throw new UserError(co.S("code") + " zaten ders listesinde (veya onay bekliyor).");
        }
        var isCoord = u.Role == "koordinator";
        reason = (reason ?? "").Trim();
        if (isCoord && reason.Length < 3)
        {
            reason = "Koordinatör tarafından eklendi";
        }
        var state = isCoord ? "onaylandi" : "onay_bekliyor";
        var id = c.Db.Insert("student_courses", new Data
        {
            ["student_id"] = studentId,
            ["course_id"] = courseId,
            ["state"] = state,
            ["added_reason"] = reason == "" ? null : reason,
            ["is_legacy"] = legacy ? 1 : 0,
            ["created_by"] = u.Id,
            ["created_at"] = Clock.Now(),
            ["decided_by"] = isCoord ? u.Id : null,
            ["decided_at"] = isCoord ? Clock.Now() : null,
        });
        Audit.Log(c, "plan_add", "student_course", id, studentId, null, new Data { ["ders"] = co.S("code") + " " + co.S("name"), ["state"] = state }, reason == "" ? null : reason);
        if (!isCoord)
        {
            Notify.Coordinators(c, st.S("full_name") + " ders listesine " + co.S("code") + " ekledi (onay bekliyor).", "/koordinator/ogrenci/" + studentId + "?tab=liste");
        }
        return id;
    }

    /// <summary>Varsayılan ÇAP protokolündeki tüm dersleri listeye ekler</summary>
    public static int AddProtocol(IAppCtx c, long studentId)
    {
        var n = 0;
        foreach (var co in c.Db.All("SELECT id FROM courses WHERE in_cap_protocol = 1 AND is_active = 1 ORDER BY semester, code"))
        {
            if (c.Db.Scalar("SELECT id FROM student_courses WHERE student_id = @sid AND course_id = @cid AND state IN ('onay_bekliyor','onaylandi')", new { sid = studentId, cid = co.L("id") }) == null)
            {
                PlanAdd(c, studentId, co.L("id"), "Protokol şablonundan eklendi");
                n++;
            }
        }
        return n;
    }

    public static long PlanDecide(IAppCtx c, long id, bool approve, string reason = "")
    {
        var p = c.Db.One("SELECT sc.*, c.code FROM student_courses sc JOIN courses c ON c.id = sc.course_id WHERE sc.id = @id AND sc.state = 'onay_bekliyor'", new { id })
                ?? throw new UserError("Onay bekleyen kayıt bulunamadı.");
        if (!approve)
        {
            StudentService.RequireReason(reason);
        }
        var state = approve ? "onaylandi" : "reddedildi";
        c.Db.Update("student_courses", new Data { ["state"] = state, ["decided_by"] = c.User!.Id, ["decided_at"] = Clock.Now(), ["reject_reason"] = approve ? null : reason }, "id = @id", new { id });
        Audit.Log(c, approve ? "plan_approve" : "plan_reject", "student_course", id, p.L("student_id"), new Data { ["state"] = "onay_bekliyor" }, new Data { ["state"] = state, ["ders"] = p.S("code") }, reason == "" ? null : reason);
        Notify.Student(c, p.L("student_id"), "Ders listesi: " + p.S("code") + " " + (approve ? "onaylandı." : "reddedildi. Gerekçe: " + reason), "/ogrenci/dersler");
        return p.L("student_id");
    }

    /// <summary>Koordinatör listeden ders çıkarır. Geçilmiş ders ve notlar etkilenmez.</summary>
    public static long PlanRemove(IAppCtx c, long id, string reason)
    {
        var p = c.Db.One("SELECT sc.*, c.code FROM student_courses sc JOIN courses c ON c.id = sc.course_id WHERE sc.id = @id AND sc.state = 'onaylandi'", new { id })
                ?? throw new UserError("Listede onaylı kayıt bulunamadı.");
        StudentService.RequireReason(reason);
        c.Db.Update("student_courses", new Data { ["state"] = "cikarildi", ["removed_reason"] = reason, ["removed_by"] = c.User!.Id, ["removed_at"] = Clock.Now() }, "id = @id", new { id });
        Audit.Log(c, "plan_remove", "student_course", id, p.L("student_id"), new Data { ["state"] = "onaylandi" }, new Data { ["state"] = "cikarildi", ["ders"] = p.S("code") }, reason);
        Notify.Student(c, p.L("student_id"), "Ders listenizden " + p.S("code") + " çıkarıldı. Gerekçe: " + reason, "/ogrenci/dersler");
        return p.L("student_id");
    }

    /// <summary>Öğrenci, henüz onaylanmamış talebini geri çeker (kayıt silinmez, iptal olur)</summary>
    public static void PlanWithdraw(IAppCtx c, long id, long studentId)
    {
        var p = c.Db.One("SELECT * FROM student_courses WHERE id = @id AND student_id = @sid AND state = 'onay_bekliyor'", new { id, sid = studentId })
                ?? throw new UserError("Yalnızca onay bekleyen kendi kayıtlarınızı geri çekebilirsiniz.");
        c.Db.Update("student_courses", new Data { ["state"] = "iptal", ["reject_reason"] = "Öğrenci tarafından geri çekildi" }, "id = @id", new { id });
        Audit.Log(c, "plan_withdraw", "student_course", id, studentId, new Data { ["state"] = "onay_bekliyor" }, new Data { ["state"] = "iptal" });
    }

    // ================= Katalog dışı ders talebi =================

    public static void RequestCourse(IAppCtx c, long studentId, string code, string name, string explanation)
    {
        name = (name ?? "").Trim();
        explanation = (explanation ?? "").Trim();
        code = (code ?? "").Trim().ToUpperInvariant();
        if (name.Length < 3 || explanation.Length < 10)
        {
            throw new UserError("Ders adı ve en az 10 karakterlik açıklama girilmelidir.");
        }
        var id = c.Db.Insert("course_requests", new Data
        {
            ["student_id"] = studentId,
            ["course_code"] = code == "" ? null : (code.Length > 20 ? code[..20] : code),
            ["course_name"] = name.Length > 200 ? name[..200] : name,
            ["explanation"] = explanation.Length > 1000 ? explanation[..1000] : explanation,
            ["state"] = "bekliyor",
            ["created_at"] = Clock.Now(),
        });
        Audit.Log(c, "course_request", "course_request", id, studentId, null, new Data { ["ders"] = code + " " + name });
        Notify.Coordinators(c, "Katalogda olmayan ders talebi: " + name, "/koordinator/ders-talepleri");
    }

    public static void DecideRequest(IAppCtx c, long id, bool accept, string response, IDictionary<string, string>? courseData)
    {
        var r = c.Db.One("SELECT * FROM course_requests WHERE id = @id AND state = 'bekliyor'", new { id }) ?? throw new UserError("Bekleyen talep bulunamadı.");
        long? courseId = null;
        if (accept)
        {
            var data = courseData ?? new Dictionary<string, string>();
            var existing = c.Db.Scalar("SELECT id FROM courses WHERE code = @code", new { code = G(data, "code").ToUpperInvariant() });
            courseId = existing != null ? Convert.ToInt64(existing) : CreateCourse(c, data);
        }
        else
        {
            StudentService.RequireReason(response);
        }
        c.Db.Update("course_requests", new Data
        {
            ["state"] = accept ? "eklendi" : "reddedildi", ["response"] = string.IsNullOrWhiteSpace(response) ? null : response,
            ["created_course_id"] = courseId, ["decided_by"] = c.User!.Id, ["decided_at"] = Clock.Now(),
        }, "id = @id", new { id });
        Audit.Log(c, "course_request_decide", "course_request", id, r.L("student_id"), new Data { ["state"] = "bekliyor" },
            new Data { ["state"] = accept ? "eklendi" : "reddedildi", ["course_id"] = courseId }, string.IsNullOrWhiteSpace(response) ? null : response);
        Notify.Student(c, r.L("student_id"), "Ders talebiniz (" + r.S("course_name") + ") " + (accept ? "kabul edildi; ders kataloğa eklendi." : "reddedildi: " + response), "/ogrenci/dersler");
    }
}
