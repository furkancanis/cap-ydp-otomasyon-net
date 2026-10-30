using System.Text;
using System.Text.RegularExpressions;

namespace CapYdp.Seed;

/// <summary>
/// Kurulum verisi + SENTETİK test verisi.
/// UYARI: Buradaki tüm kişi, numara ve belgeler hayalidir (sentetik). Gerçek öğrenci verisi kullanılmaz.
/// </summary>
public static class Seeder
{
    /// <summary>Veritabanı boşsa şemayı kurar ve verileri yükler</summary>
    public static bool InstallIfEmpty(string contentRoot, bool baseOnly)
    {
        using var db = new Db();
        db.Migrate(contentRoot);
        if (db.Count("SELECT COUNT(*) FROM users") > 0)
        {
            return false;
        }
        using var c = new SeedCtx(db);
        try
        {
            db.Tx(() => Run(c, baseOnly));
        }
        catch
        {
            // eşzamanlı ilk açılışta başka süreç kurmuş olabilir
            if (db.Count("SELECT COUNT(*) FROM users") == 0)
            {
                throw;
            }
        }
        return true;
    }

    private static Dictionary<string, string> D(params (string K, object? V)[] kv) =>
        kv.ToDictionary(x => x.K, x => Convert.ToString(x.V, System.Globalization.CultureInfo.InvariantCulture) ?? "");

    public static void Run(SeedCtx c, bool baseOnly)
    {
        var db = c.Db;
        var t0 = Clock.Now();

        // ---------------- Ayarlar ----------------
        foreach (var (k, v, d) in new[]
                 {
                     ("max_upload_mb", "5", "En büyük dosya boyutu (MB)"),
                     ("allowed_extensions", "pdf,jpg,jpeg,png", "İzin verilen uzantılar"),
                     ("student_email_domain", "ogr.selcuk.edu.tr", "Öğrenci e-posta alan adı"),
                     ("department_name", "Bilgisayar Mühendisliği Bölümü", "Bölüm adı"),
                     ("coordinator_title", "ÇAP / YDP Koordinatörlüğü", "Koordinatörlük"),
                 })
        {
            db.InsertNoId("settings", new Data { ["setting_key"] = k, ["setting_value"] = v, ["description"] = d, ["updated_at"] = t0 });
        }

        // ---------------- Dönemler ----------------
        for (var y = 2022; y <= 2027; y++)
        {
            foreach (var s in new[] { "Güz", "Bahar" })
            {
                var ay = y + "-" + (y + 1);
                db.Insert("terms", new Data { ["academic_year"] = ay, ["season"] = s, ["sort_key"] = Terms.SortKey(ay, s), ["is_current"] = 0, ["is_active"] = 1, ["created_at"] = t0 });
            }
        }
        db.Exec("UPDATE terms SET is_current = 1 WHERE academic_year = '2026-2027' AND season = 'Güz'");
        Terms.ResetCache(c);

        // ---------------- Personel hesapları ----------------
        var coord = db.Insert("users", new Data
        {
            ["username"] = "koordinator", ["password_hash"] = Passwords.Hash("Koordinator123!"), ["role"] = "koordinator",
            ["full_name"] = "Test Koordinatör", ["email"] = "koordinator@example.com", ["is_active"] = 1, ["must_change_password"] = 0, ["created_at"] = t0,
        });
        db.Insert("users", new Data
        {
            ["username"] = "baskan", ["password_hash"] = Passwords.Hash("Baskan123!"), ["role"] = "bolum_baskani",
            ["full_name"] = "Test Bölüm Başkanı", ["email"] = "baskan@example.com", ["is_active"] = 1, ["must_change_password"] = 0, ["created_at"] = t0,
        });
        c.ActAs(coord);

        // ---------------- Yönerge kuralları (sürümlü) ----------------
        Dictionary<string, string> Enc(string type)
        {
            var r = new Dictionary<string, string>();
            foreach (var kv in Rules.Defaults(type))
            {
                r[kv.Key] = kv.Value is System.Text.Json.Nodes.JsonValue jv && jv.TryGetValue<string>(out var sv) ? sv : kv.Value!.ToJsonString();
            }
            return r;
        }
        AdminService.CreateRuleSet(c, "CAP", "ÇAP Yönergesi — Senato 26/10/2023, 2023-129", "2023-10-26", Enc("CAP"), "Önceki yönerge sürümü (arşiv).", true);
        AdminService.CreateRuleSet(c, "CAP", "ÇAP Yönergesi — Senato 20/05/2026, 2026-20", "2026-05-20", Enc("CAP"), "Yürürlükteki yönerge. Sayısal eşiklerde değişiklik yok.", true);
        AdminService.CreateRuleSet(c, "YDP", "Yandal Yönergesi — Senato 26/10/2023, 2023-131", "2023-10-26", Enc("YDP"), "Yürürlükteki yandal yönergesi.", true);

        // ---------------- Ders kataloğu ----------------
        var protocol = new (string Code, string Name, int Ects, int Sem, string Cat)[]
        {
            ("3301156", "Bilgisayar Mühendisliğine Giriş", 4, 1, "zorunlu"),
            ("3301351", "Nesneye Yönelik Programlama Dili", 4, 3, "zorunlu"),
            ("3301352", "İşletim Sistemleri", 4, 3, "zorunlu"),
            ("3301354", "Veri Yapıları ve Uygulamaları", 4, 3, "zorunlu"),
            ("3301452", "Programlama Dili-2", 4, 4, "zorunlu"),
            ("3301454", "Veri Tabanı Yönetim Sistemleri", 5, 4, "zorunlu"),
            ("3301455", "Yazılım Mühendisliği", 4, 4, "zorunlu"),
            ("3301456", "Mobil Programlama", 4, 4, "zorunlu"),
            ("3301552", "Programlama Dili-3", 5, 5, "zorunlu"),
            ("3301553", "Veri Haberleşmesi ve Ağlar", 5, 5, "zorunlu"),
            ("3301651", "Yapay Zeka", 4, 6, "zorunlu"),
            ("3301652", "Otomata Teorisi ve Biçimsel Diller", 4, 6, "zorunlu"),
            ("3301653", "Görüntü İşleme", 4, 6, "zorunlu"),
            ("3301654", "Mühendislik Tasarımı", 6, 6, "zorunlu"),
            ("3301751", "Bilgisayar Mühendisliği Uygulamaları", 4, 7, "zorunlu"),
            ("3301752", "Bilgisayar Mimarisi ve Organizasyonu", 4, 7, "zorunlu"),
            ("3301753", "Veri Bilimi", 4, 7, "zorunlu"),
            ("3301851", "İşletmede Mesleki Eğitim", 20, 8, "staj"),
            ("3301852", "Staj 1", 5, 8, "staj"),
            ("3301853", "Staj 2", 5, 8, "staj"),
        };
        foreach (var p in protocol)
        {
            CourseService.CreateCourse(c, D(("code", p.Code), ("name", p.Name), ("ects", p.Ects), ("credit", 0), ("semester", p.Sem), ("category", p.Cat),
                ("program_scope", "HER_IKISI"), ("in_cap_protocol", 1), ("description", "EEM → BM ÇAP protokolü")));
        }
        var electives = new (string Code, string Name, int Sem, string Desc)[]
        {
            ("3301561", "Sinyal İşleme", 5, "Protokol dipnotu 2"),
            ("3301568", "Veri Madenciliği", 5, "Protokol dipnotu 3"),
            ("3301563", "Bilgisayar Grafiği", 5, "Örnek seçmeli (sentetik)"),
            ("3301565", "Web Programlama", 5, "Örnek seçmeli (sentetik)"),
            ("3301663", "Gömülü Sistemler", 6, "Protokol dipnotu 6"),
            ("3301665", "Robotik", 6, "Protokol dipnotu 7"),
            ("3301661", "Bulut Bilişim", 6, "Örnek seçmeli (sentetik)"),
            ("3301667", "Bilgi Güvenliği", 6, "Örnek seçmeli (sentetik)"),
            ("3301770", "Veri İletişim Teknikleri", 7, "Protokol dipnotu 8"),
            ("3301771", "Derin Öğrenme", 7, "Örnek seçmeli (sentetik)"),
            ("3301773", "Dağıtık Sistemler", 7, "Örnek seçmeli (sentetik)"),
        };
        foreach (var e in electives)
        {
            CourseService.CreateCourse(c, D(("code", e.Code), ("name", e.Name), ("ects", 4), ("credit", 0), ("semester", e.Sem), ("category", "teknik_secmeli"),
                ("program_scope", "HER_IKISI"), ("in_cap_protocol", 0), ("description", e.Desc)));
        }

        if (!baseOnly)
        {
            Students(c, coord);
        }
    }

    // ================== SENTETİK ÖĞRENCİLER ==================

    private static long Term(SeedCtx c, string label) => Terms.Resolve(c, label)!.L("id");

    private static long Cid(SeedCtx c, string code) => c.Db.Count("SELECT id FROM courses WHERE code = @code", new { code });

    private static long StudentUser(SeedCtx c, long sid) => c.Db.Count("SELECT id FROM users WHERE student_id = @sid", new { sid });

    private static CheckedFile MakePdf(SeedCtx c, string name, string text) => FileGuard.Check(c, name, Encoding.ASCII.GetBytes(Pdf(text)));

    /// <summary>Basit, geçerli tek sayfalık PDF üretir</summary>
    public static string Pdf(string text)
    {
        text = Regex.Replace(text, @"[^A-Za-z0-9 .,:/\-()]", "");
        var lines = new[] { "SELCUK UNIVERSITESI - BILGISAYAR MUHENDISLIGI", "CAP / YDP OGRENCI TAKIP OTOMASYONU", "", text, "", "Bu belge sentetik test verisidir. Gercek kisiye ait degildir." };
        var stream = new StringBuilder("BT /F1 12 Tf 60 780 Td 16 TL\n");
        foreach (var l in lines)
        {
            stream.Append('(').Append(l).Append(") Tj T*\n");
        }
        stream.Append("ET");
        var objs = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Length " + stream.Length + " >>\nstream\n" + stream + "\nendstream",
        };
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objs.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(i + 1).Append(" 0 obj\n").Append(objs[i]).Append("\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append("xref\n0 ").Append(objs.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var off in offsets)
        {
            pdf.Append(off.ToString("D10")).Append(" 00000 n \n");
        }
        pdf.Append("trailer\n<< /Size ").Append(objs.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return pdf.ToString();
    }

    private static void Legacy(SeedCtx c, long sid, params (string Term, string Code, string Status, string? Grade)[] rows)
    {
        foreach (var r in rows)
        {
            EnrollmentService.CoordinatorAdd(c, sid, Cid(c, r.Code), Term(c, r.Term), r.Status, r.Grade, true, "Geriye dönük kayıt (ilk kurulum)");
        }
    }

    private static void SetCurrent(SeedCtx c, string year, string season)
    {
        c.Db.Exec("UPDATE terms SET is_current = 0");
        c.Db.Exec("UPDATE terms SET is_current = 1 WHERE academic_year = @year AND season = @season", new { year, season });
        Terms.ResetCache(c);
    }

    private static void Students(SeedCtx c, long coord)
    {
        var db = c.Db;
        var pw = Passwords.Hash("Ogrenci123!");
        long Mk(string no, string name, string email, string type, string term, string faculty, string dept)
        {
            var r = StudentService.Upsert(c, new StudentService.StudentInput
            {
                StudentNo = no, FullName = name, Email = email, ProgramType = type, AdmissionTermId = Term(c, term),
                MainFaculty = faculty, MainDepartment = dept,
            });
            db.Exec("UPDATE users SET password_hash = @pw, must_change_password = 0 WHERE student_id = @sid", new { pw, sid = r.Id });
            return r.Id;
        }

        // ---------- 1) ÇAP — mevcut öğrenci, geriye dönük kayıtlı, ilerlemiş ----------
        c.ActAs(coord);
        var s1 = Mk("222101001", "Elif Kaya", "elif.kaya@ogr.selcuk.edu.tr", "CAP", "2024-2025 Güz", "Teknoloji Fakültesi", "Elektrik-Elektronik Mühendisliği");
        CourseService.AddProtocol(c, s1);
        foreach (var code in new[] { "3301561", "3301568", "3301565", "3301663", "3301665", "3301661", "3301770", "3301771" })
        {
            CourseService.PlanAdd(c, s1, Cid(c, code), "Protokol teknik seçmeli tercihi", true);
        }
        Legacy(c, s1,
            ("2024-2025 Güz", "3301156", "gecti", "BA"), ("2024-2025 Güz", "3301351", "gecti", "CB"),
            ("2024-2025 Bahar", "3301352", "gecti", "BB"), ("2024-2025 Bahar", "3301354", "gecti", "DD"), ("2024-2025 Bahar", "3301452", "kaldi", "FF"),
            ("2025-2026 Güz", "3301455", "gecti", "AA"), ("2025-2026 Güz", "3301552", "gecti", "CC"), ("2025-2026 Güz", "3301553", "gecti", "BA"), ("2025-2026 Güz", "3301561", "gecti", "BB"),
            ("2025-2026 Bahar", "3301452", "gecti", "CB"), ("2025-2026 Bahar", "3301454", "gecti", "BB"), ("2025-2026 Bahar", "3301456", "gecti", "BA"), ("2025-2026 Bahar", "3301651", "gecti", "CC"),
            ("2026-2027 Güz", "3301751", "alindi", null));
        StudentService.AddGano(c, s1, Term(c, "2024-2025 Güz"), 3.10m, "Ana dal transkripti");
        StudentService.AddGano(c, s1, Term(c, "2025-2026 Güz"), 3.02m, "Ana dal transkripti");
        StudentService.AddGano(c, s1, Term(c, "2025-2026 Bahar"), 2.95m, "Öğrenci İşleri yazısı");
        c.ActAs(StudentUser(c, s1));
        var d1 = DocumentService.Store(c, s1, "kabul_protokol_formu", Term(c, "2024-2025 Güz"), "ÇAP Kabul ve Ders Protokol Formu", MakePdf(c, "protokol_formu_imzali.pdf", "CAP KABUL VE DERS PROTOKOL FORMU - IMZALI"));
        c.ActAs(coord);
        DocumentService.Decide(c, d1, true);
        c.ActAs(StudentUser(c, s1));
        EnrollmentService.StudentAdd(c, s1, Cid(c, "3301752"), Term(c, "2026-2027 Güz"));
        EnrollmentService.StudentAdd(c, s1, Cid(c, "3301753"), Term(c, "2026-2027 Güz"));
        DocumentService.Store(c, s1, "ders_kayit_formu", Term(c, "2026-2027 Güz"), "2026-2027 Güz Ders Kayıt Formu", MakePdf(c, "ders_kayit_2026_guz.pdf", "CAP DERS KAYIT FORMU 2026-2027 GUZ"));
        MessageService.Send(c, s1, "Hocam merhaba, bu dönem Bilgisayar Mimarisi ve Veri Bilimi derslerini ekledim. Ders kayıt formumu da yükledim, kontrol edebilir misiniz?");
        c.ActAs(coord);
        MessageService.Send(c, s1, "Merhaba Elif, formunu inceliyorum. Danışman imzası net görünmüyor; gerekirse yeniden yüklemeni isteyeceğim.");
        StudentService.AddNote(c, s1, "Öğrenci düzenli; Programlama Dili-2 dersini ikinci denemede geçti. Staj planlaması 2027 yazında konuşulacak.");

        // ---------- 2) ÇAP — yeni kabul edilmiş öğrenci ----------
        c.ActAs(coord);
        var s2 = Mk("232101002", "Mehmet Demir", "mehmet.demir@ogr.selcuk.edu.tr", "CAP", "2026-2027 Güz", "Teknoloji Fakültesi", "Elektrik-Elektronik Mühendisliği");
        StudentService.AddGano(c, s2, Term(c, "2025-2026 Bahar"), 3.21m, "Dekanlık kabul yazısı ekindeki transkript");
        c.ActAs(StudentUser(c, s2));
        DocumentService.Store(c, s2, "kabul_protokol_formu", Term(c, "2026-2027 Güz"), "ÇAP Kabul ve Ders Protokol Formu", MakePdf(c, "protokol_mehmet.pdf", "CAP KABUL VE DERS PROTOKOL FORMU"));
        foreach (var code in new[] { "3301156", "3301351", "3301352", "3301354" })
        {
            CourseService.PlanAdd(c, s2, Cid(c, code));
        }
        CourseService.RequestCourse(c, s2, "3302458", "Veri Yapıları (EEM)", "Protokol dipnotu 1 gereği EEM bölümünden Veri Yapıları dersini alırsam 3301354 dersinden muaf sayılacağım. Bu dersin kataloğa eklenmesini rica ederim.");

        // ---------- 3) YDP — normal akışla ilerleyen öğrenci ----------
        c.ActAs(coord);
        var s3 = Mk("222101003", "Zeynep Arslan", "zeynep.arslan@ogr.selcuk.edu.tr", "YDP", "2025-2026 Güz", "Mühendislik ve Doğa Bilimleri Fakültesi", "Endüstri Mühendisliği");
        foreach (var code in new[] { "3301156", "3301351", "3301354", "3301454", "3301455", "3301456", "3301553" })
        {
            CourseService.PlanAdd(c, s3, Cid(c, code), "Yandal ders seçimi (koordinatörle birlikte)");
        }
        StudentService.AddGano(c, s3, Term(c, "2025-2026 Güz"), 2.91m, "Ana dal transkripti");
        StudentService.AddGano(c, s3, Term(c, "2025-2026 Bahar"), 2.85m, "Ana dal transkripti");
        Legacy(c, s3, ("2025-2026 Güz", "3301156", "gecti", "BB"), ("2025-2026 Güz", "3301351", "gecti", "CC"));
        // 2025-2026 Bahar: normal akış (öğrenci girer → onay → transkript → not → onay)
        SetCurrent(c, "2025-2026", "Bahar");
        c.ActAs(StudentUser(c, s3));
        var e1 = EnrollmentService.StudentAdd(c, s3, Cid(c, "3301354"), Term(c, "2025-2026 Bahar"));
        var e2 = EnrollmentService.StudentAdd(c, s3, Cid(c, "3301454"), Term(c, "2025-2026 Bahar"));
        c.ActAs(coord);
        EnrollmentService.Decide(c, e1, true);
        EnrollmentService.Decide(c, e2, true);
        c.ActAs(StudentUser(c, s3));
        DocumentService.Store(c, s3, "cap_ydp_transkript", Term(c, "2025-2026 Bahar"), "YDP Not Belgesi 2025-2026 Bahar", MakePdf(c, "ydp_transkript_bahar.pdf", "YANDAL NOT BELGESI 2025-2026 BAHAR"));
        EnrollmentService.SubmitGrades(c, s3, Term(c, "2025-2026 Bahar"), new Dictionary<long, string> { [e1] = "CB", [e2] = "BA" });
        c.ActAs(coord);
        foreach (var g in db.All("SELECT id FROM grade_submissions WHERE student_id = @sid", new { sid = s3 }))
        {
            EnrollmentService.DecideGrade(c, g.L("id"), true);
        }
        DocumentService.Decide(c, db.Count("SELECT id FROM documents WHERE student_id = @sid AND doc_type = 'cap_ydp_transkript'", new { sid = s3 }), true);
        SetCurrent(c, "2026-2027", "Güz");
        c.ActAs(StudentUser(c, s3));
        var e3 = EnrollmentService.StudentAdd(c, s3, Cid(c, "3301455"), Term(c, "2026-2027 Güz"));
        c.ActAs(coord);
        EnrollmentService.Decide(c, e3, true);

        // ---------- 4) YDP — uyarı üreten öğrenci ----------
        c.ActAs(coord);
        var s4 = Mk("212101004", "Can Öztürk", "can.ozturk@ogr.selcuk.edu.tr", "YDP", "2024-2025 Güz", "Teknoloji Fakültesi", "Mekatronik Mühendisliği");
        foreach (var code in new[] { "3301156", "3301351", "3301352", "3301354", "3301452", "3301454" })
        {
            CourseService.PlanAdd(c, s4, Cid(c, code), "Yandal ders seçimi");
        }
        Legacy(c, s4,
            ("2024-2025 Güz", "3301156", "gecti", "DD"), ("2024-2025 Güz", "3301351", "kaldi", "FF"),
            ("2024-2025 Bahar", "3301352", "kaldi", "FD"), ("2024-2025 Bahar", "3301354", "gecti", "DC"));
        StudentService.AddGano(c, s4, Term(c, "2024-2025 Bahar"), 2.41m, "Ana dal transkripti");
        StudentService.AddGano(c, s4, Term(c, "2025-2026 Bahar"), 2.21m, "Öğrenci İşleri");
        DocumentService.CreateRequest(c, s4, "anadal_transkript", "Güncel ana dal transkriptinizi yükleyiniz. GANO 2,30 altına düştüğü görülüyor; teyit için gereklidir.", "2026-11-15");
        MessageService.Send(c, s4, "Can, son iki dönem yandal programından ders almadığın görülüyor. Yönergeye göre üst üste iki dönem ders almayan öğrencinin kaydı silinir. Mazeretin varsa belgeleriyle iletmeni rica ederim.");

        // ---------- 5) ÇAP — GANO bir kez eşik altına düşmüş öğrenci ----------
        c.ActAs(coord);
        var s5 = Mk("222101005", "Ayşe Yıldız", "ayse.yildiz@ogr.selcuk.edu.tr", "CAP", "2025-2026 Güz", "Teknoloji Fakültesi", "Elektrik-Elektronik Mühendisliği");
        CourseService.AddProtocol(c, s5);
        Legacy(c, s5,
            ("2025-2026 Güz", "3301156", "gecti", "CC"), ("2025-2026 Güz", "3301351", "gecti", "CB"),
            ("2025-2026 Bahar", "3301352", "gecti", "BB"), ("2025-2026 Bahar", "3301354", "muaf", null));
        StudentService.AddGano(c, s5, Term(c, "2025-2026 Güz"), 2.80m, "Ana dal transkripti");
        StudentService.AddGano(c, s5, Term(c, "2025-2026 Bahar"), 2.68m, "Ana dal transkripti");
        c.ActAs(StudentUser(c, s5));
        EnrollmentService.StudentAdd(c, s5, Cid(c, "3301452"), Term(c, "2026-2027 Güz"));
        DocumentService.Store(c, s5, "muafiyet_formu", Term(c, "2025-2026 Bahar"), "Özel Şartlı Ders Muafiyet Formu (3301354)", MakePdf(c, "muafiyet_formu.pdf", "OZEL SARTLI DERS MUAFIYET FORMU"));

        // ---------- Kural kontrolleri ----------
        c.ActAs(coord);
        foreach (var sid in new[] { s1, s2, s3, s4, s5 })
        {
            Rules.Snapshot(c, sid, "İlk kurulum kontrolü");
        }
    }
}
