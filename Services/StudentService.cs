using System.Text;
using System.Text.RegularExpressions;

namespace CapYdp.Services;

public sealed record UpsertResult(long Id, bool Created, string? Username = null, string? Password = null);

public sealed class ImportResult
{
    public List<(string StudentNo, string Password)> Created { get; } = new();
    public int Updated { get; set; }
    public List<string> Errors { get; } = new();
}

/// <summary>
/// Öğrenci veri girişinin TEK giriş noktası.
/// Form, CSV içe aktarma ve ileride eklenecek OBİS entegrasyonu aynı Upsert() fonksiyonunu kullanır.
/// Benzersiz anahtar: öğrenci numarası.
/// </summary>
public static class StudentService
{
    public sealed class StudentInput
    {
        public string StudentNo { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string ProgramType { get; set; } = "";
        public long AdmissionTermId { get; set; }
        public string? MainFaculty { get; set; }
        public string? MainDepartment { get; set; }
        public int MainProgramYears { get; set; } = 4;
    }

    private static string Cut(string? s, int n)
    {
        s = (s ?? "").Trim();
        return s.Length > n ? s[..n] : s;
    }

    public static UpsertResult Upsert(IAppCtx c, StudentInput d, string source = "manuel", string? externalRef = null)
    {
        var no = Regex.Replace(d.StudentNo ?? "", @"\s+", "");
        var name = (d.FullName ?? "").Trim();
        var email = (d.Email ?? "").Trim().ToLowerInvariant();
        var type = (d.ProgramType ?? "").ToUpperInvariant().Replace("Ç", "C");

        if (!Regex.IsMatch(no, @"^\d{6,15}$"))
        {
            throw new UserError("Öğrenci numarası 6-15 haneli rakamlardan oluşmalıdır.");
        }
        if (name.Length < 3 || name.Length > 150)
        {
            throw new UserError("Ad soyad 3-150 karakter olmalıdır.");
        }
        if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            throw new UserError("Geçerli bir okul e-posta adresi girin.");
        }
        var domain = Settings.Get(c, "student_email_domain", "");
        if (domain != "" && !email.EndsWith("@" + domain))
        {
            throw new UserError($"E-posta adresi @{domain} ile bitmelidir.");
        }
        if (type is not ("CAP" or "YDP"))
        {
            throw new UserError("Program türü ÇAP veya YDP olmalıdır.");
        }
        if (Terms.Find(c, d.AdmissionTermId) == null)
        {
            throw new UserError("Kabul dönemi seçilmelidir.");
        }
        var faculty = Cut(d.MainFaculty, 150);
        var department = Cut(d.MainDepartment, 150);
        var row = new Data
        {
            ["full_name"] = name,
            ["email"] = email,
            ["program_type"] = type,
            ["admission_term_id"] = d.AdmissionTermId,
            ["main_faculty"] = faculty == "" ? null : faculty,
            ["main_department"] = department == "" ? null : department,
            ["main_program_years"] = Math.Clamp(d.MainProgramYears, 2, 6),
        };

        return c.Db.Tx(() =>
        {
            var existing = c.Db.One("SELECT * FROM students WHERE student_no = @no", new { no });
            if (existing != null)
            {
                if (existing.S("program_type") != type)
                {
                    throw new UserError($"{no} numaralı öğrenci sistemde {L.ProgramShort[existing.S("program_type")]} öğrencisi olarak kayıtlı. Program türü bu yolla değiştirilemez.");
                }
                var (o, n) = Audit.Diff(existing, row);
                if (n.Count > 0)
                {
                    var upd = new Data(n) { ["updated_at"] = Clock.Now() };
                    c.Db.Update("students", upd, "id = @id", new { id = existing.L("id") });
                    Audit.Log(c, "student_update", "student", existing.L("id"), existing.L("id"), o, n, "Kaynak: " + source);
                }
                return new UpsertResult(existing.L("id"), false);
            }
            var ins = new Data(row)
            {
                ["student_no"] = no,
                ["status"] = "aktif",
                ["data_source"] = source,
                ["external_ref"] = externalRef,
                ["created_by"] = c.User?.Id,
                ["created_at"] = Clock.Now(),
                ["updated_at"] = Clock.Now(),
            };
            var id = c.Db.Insert("students", ins);
            var logged = new Data(row) { ["student_no"] = no, ["data_source"] = source };
            Audit.Log(c, "student_create", "student", id, id, null, logged);

            // Öğrenci hesabını koordinatör açar (öğrenci kendi kaydolamaz)
            var pw = Passwords.Random();
            var uid = c.Db.Insert("users", new Data
            {
                ["username"] = no,
                ["password_hash"] = Passwords.Hash(pw),
                ["role"] = "ogrenci",
                ["full_name"] = name,
                ["email"] = email,
                ["student_id"] = id,
                ["is_active"] = 1,
                ["must_change_password"] = 1,
                ["created_at"] = Clock.Now(),
            });
            Audit.Log(c, "user_create", "user", uid, id, null, new Data { ["username"] = no, ["role"] = "ogrenci" });
            return new UpsertResult(id, true, no, pw);
        });
    }

    public static string StripBom(string s) => s.StartsWith('﻿') ? s[1..] : s;

    /// <summary>Basit CSV satır ayrıştırıcı (tırnaklı alanlar desteklenir)</summary>
    public static List<string> CsvLine(string line, char sep)
    {
        var res = new List<string>();
        var sb = new StringBuilder();
        var q = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (q)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (ch == '"') q = false;
                else sb.Append(ch);
            }
            else if (ch == '"') q = true;
            else if (ch == sep) { res.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        res.Add(sb.ToString());
        return res;
    }

    /// <summary>CSV satırlarını başlıklı sözlüklere çevirir</summary>
    public static (List<string> Header, List<(int LineNo, Dictionary<string, string>? Row)> Rows) ParseCsv(string csv)
    {
        var lines = Regex.Split(StripBom(csv).Trim(), "\r\n|\n|\r");
        var first = lines.Length > 0 ? lines[0] : "";
        var sep = first.Count(ch => ch == ';') >= first.Count(ch => ch == ',') ? ';' : ',';
        var header = CsvLine(first, sep).Select(h => h.Trim().ToLowerInvariant()).ToList();
        var rows = new List<(int, Dictionary<string, string>?)>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "")
            {
                continue;
            }
            var vals = CsvLine(lines[i], sep);
            if (vals.Count > header.Count)
            {
                rows.Add((i + 1, null));
                continue;
            }
            var d = new Dictionary<string, string>();
            for (var j = 0; j < header.Count; j++)
            {
                d[header[j]] = j < vals.Count ? vals[j] : "";
            }
            rows.Add((i + 1, d));
        }
        return (header, rows);
    }

    /// <summary>CSV: student_no;full_name;email;program_type;admission_term;main_faculty;main_department</summary>
    public static ImportResult ImportCsv(IAppCtx c, string csv)
    {
        var res = new ImportResult();
        var (header, rows) = ParseCsv(csv);
        if (!header.Contains("student_no"))
        {
            throw new UserError("CSV ilk satırı başlık olmalı ve student_no sütunu içermelidir.");
        }
        foreach (var (lineNo, r) in rows)
        {
            if (r == null)
            {
                res.Errors.Add($"Satır {lineNo}: sütun sayısı hatalı");
                continue;
            }
            try
            {
                var term = Terms.Resolve(c, r.GetValueOrDefault("admission_term", ""), true);
                var u = Upsert(c, new StudentInput
                {
                    StudentNo = r.GetValueOrDefault("student_no", ""),
                    FullName = r.GetValueOrDefault("full_name", ""),
                    Email = r.GetValueOrDefault("email", ""),
                    ProgramType = r.GetValueOrDefault("program_type", ""),
                    AdmissionTermId = term?.L("id") ?? 0,
                    MainFaculty = r.GetValueOrDefault("main_faculty"),
                    MainDepartment = r.GetValueOrDefault("main_department"),
                }, "csv");
                if (u.Created)
                {
                    res.Created.Add((u.Username!, u.Password!));
                }
                else
                {
                    res.Updated++;
                }
            }
            catch (UserError e)
            {
                res.Errors.Add($"Satır {lineNo}: {e.Message}");
            }
        }
        Audit.Log(c, "student_import", "student", null, null, null, new Data { ["created"] = res.Created.Count, ["updated"] = res.Updated, ["errors"] = res.Errors.Count });
        return res;
    }

    // ---------------- GANO ----------------

    public static void AddGano(IAppCtx c, long studentId, long termId, decimal? gano, string source, string note = "")
    {
        if (gano == null || gano < 0 || gano > 4)
        {
            throw new UserError("GANO 0,00 ile 4,00 arasında olmalıdır.");
        }
        if (Terms.Find(c, termId) == null)
        {
            throw new UserError("Dönem seçilmelidir.");
        }
        if ((source ?? "").Trim().Length < 3)
        {
            throw new UserError("GANO değerinin kaynağı (ör. transkript, öğrenci işleri) yazılmalıdır.");
        }
        var row = new Data
        {
            ["student_id"] = studentId, ["term_id"] = termId, ["gano"] = Math.Round(gano.Value, 2),
            ["source"] = Cut(source, 100), ["note"] = string.IsNullOrWhiteSpace(note) ? null : Cut(note, 500),
            ["state"] = "gecerli", ["entered_by"] = c.User!.Id, ["created_at"] = Clock.Now(),
        };
        var id = c.Db.Insert("gano_records", row);
        Audit.Log(c, "gano_add", "gano", id, studentId, null, row);
        Rules.Snapshot(c, studentId, "GANO girişi");
    }

    public static long CancelGano(IAppCtx c, long id, string reason)
    {
        var g = c.Db.One("SELECT * FROM gano_records WHERE id = @id AND state = 'gecerli'", new { id }) ?? throw new UserError("Kayıt bulunamadı.");
        RequireReason(reason);
        c.Db.Update("gano_records", new Data { ["state"] = "iptal", ["cancel_reason"] = reason }, "id = @id", new { id });
        Audit.Log(c, "gano_cancel", "gano", id, g.L("student_id"), new Data { ["state"] = "gecerli" }, new Data { ["state"] = "iptal" }, reason);
        return g.L("student_id");
    }

    // ---------------- Koordinatör notları ----------------

    public static void AddNote(IAppCtx c, long studentId, string body)
    {
        if ((body ?? "").Trim().Length < 2)
        {
            throw new UserError("Not boş olamaz.");
        }
        var id = c.Db.Insert("coordinator_notes", new Data
        {
            ["student_id"] = studentId, ["author_id"] = c.User!.Id, ["body"] = Cut(body, 5000), ["state"] = "aktif", ["created_at"] = Clock.Now(),
        });
        Audit.Log(c, "note_add", "note", id, studentId, null, new Data { ["body"] = Cut(body, 200) });
    }

    public static long CancelNote(IAppCtx c, long id, string reason)
    {
        var n = c.Db.One("SELECT * FROM coordinator_notes WHERE id = @id AND state = 'aktif'", new { id }) ?? throw new UserError("Not bulunamadı.");
        RequireReason(reason);
        c.Db.Update("coordinator_notes", new Data { ["state"] = "iptal" }, "id = @id", new { id });
        Audit.Log(c, "note_cancel", "note", id, n.L("student_id"), new Data { ["state"] = "aktif" }, new Data { ["state"] = "iptal" }, reason);
        return n.L("student_id");
    }

    // ---------------- Kararlar ----------------

    public static void Decide(IAppCtx c, long studentId, string type, long? termId, string date, string reference, string reason, long? documentId)
    {
        var s = c.Db.One("SELECT * FROM students WHERE id = @id", new { id = studentId })!;
        if (!L.Decision.ContainsKey(type))
        {
            throw new UserError("Geçersiz karar türü.");
        }
        if (type == "mezuniyet" && s.S("program_type") != "CAP")
        {
            throw new UserError("Mezuniyet (diploma) kararı yalnızca ÇAP öğrencileri içindir. YDP için sertifika kararı verin.");
        }
        if (type == "sertifika" && s.S("program_type") != "YDP")
        {
            throw new UserError("Sertifika kararı yalnızca YDP öğrencileri içindir.");
        }
        if (type == "kayit_dondurma" && termId == null)
        {
            throw new UserError("Kayıt dondurma kararında dönem seçilmelidir.");
        }
        if (!DateTime.TryParseExact(date ?? "", "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var decisionDate))
        {
            throw new UserError("Karar tarihi geçersiz.");
        }
        RequireReason(reason);
        if (documentId != null && c.Db.Scalar("SELECT id FROM documents WHERE id = @id AND student_id = @sid", new { id = documentId, sid = studentId }) == null)
        {
            throw new UserError("Seçilen belge bu öğrenciye ait değil.");
        }
        var nw = type switch
        {
            "mezuniyet" => "mezun",
            "sertifika" => "sertifika",
            "kayit_silme" => "kaydi_silindi",
            "kayit_dondurma" => "donduruldu",
            "dondurma_kaldirma" => "aktif",
            _ => "ayrildi",
        };
        // geçmiş bir dönem için dondurma girildiyse öğrencinin durumu değişmez
        var cur = Terms.Current(c);
        if (type == "kayit_dondurma" && cur != null && termId != cur.L("id"))
        {
            nw = s.S("status");
        }
        c.Db.Tx(() =>
        {
            var id = c.Db.Insert("decisions", new Data
            {
                ["student_id"] = studentId, ["decision_type"] = type, ["term_id"] = termId, ["decision_date"] = decisionDate,
                ["reference_no"] = string.IsNullOrWhiteSpace(reference) ? null : Cut(reference, 100), ["reason"] = Cut(reason, 1000), ["document_id"] = documentId,
                ["previous_status"] = s.S("status"), ["new_status"] = nw, ["state"] = "gecerli",
                ["decided_by"] = c.User!.Id, ["created_at"] = Clock.Now(),
            });
            if (nw != s.S("status"))
            {
                c.Db.Update("students", new Data { ["status"] = nw, ["updated_at"] = Clock.Now() }, "id = @id", new { id = studentId });
            }
            Audit.Log(c, "decision", "decision", id, studentId, new Data { ["status"] = s.S("status") },
                new Data { ["status"] = nw, ["karar"] = L.Decision[type], ["ref"] = reference }, reason);
        });
        Notify.Student(c, studentId, "Koordinatör kararı: " + L.Decision[type], "/ogrenci");
        Rules.Snapshot(c, studentId, "Karar: " + L.Decision[type], false);
    }

    public static void RequireReason(string? reason)
    {
        if ((reason ?? "").Trim().Length < 5)
        {
            throw new UserError("Gerekçe en az 5 karakter olmalıdır.");
        }
    }
}
