using System.Globalization;
using System.Text.Json.Nodes;

namespace CapYdp.Services;

/// <summary>Bir kural sürümünün parametreleri (JSON nesnesi üzerinde tipli erişim)</summary>
public sealed class RuleParams
{
    public JsonObject Obj { get; }

    public RuleParams(JsonObject obj) => Obj = obj;

    public decimal N(string k)
    {
        var n = Obj[k];
        if (n == null) return 0m;
        if (n is JsonValue v)
        {
            if (v.TryGetValue<decimal>(out var d)) return d;
            if (v.TryGetValue<string>(out var s) && decimal.TryParse(s.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var d2)) return d2;
        }
        return 0m;
    }

    public int I(string k) => (int)N(k);

    public string Str(string k)
    {
        var n = Obj[k];
        if (n is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        return n?.ToJsonString() ?? "";
    }

    public List<string> List(string k)
    {
        var r = new List<string>();
        if (Obj[k] is JsonArray a)
        {
            foreach (var x in a)
            {
                if (x is JsonValue v && v.TryGetValue<string>(out var s)) r.Add(s);
            }
        }
        return r;
    }

    /// <summary>{"AA":4.0, ...} veya {"5":3, ...}</summary>
    public Dictionary<string, decimal> Map(string k)
    {
        var r = new Dictionary<string, decimal>();
        if (Obj[k] is JsonObject o)
        {
            foreach (var kv in o)
            {
                if (kv.Value is JsonValue v && v.TryGetValue<decimal>(out var d)) r[kv.Key] = d;
            }
        }
        return r;
    }

    public Dictionary<string, decimal> Scale => Map("not_olcegi");
    public List<string> PassGrades => List("gecer_notlar");
    public List<string> CondGrades => List("kosullu_notlar");

    /// <summary>Formda gösterim için değer metni</summary>
    public string Display(string k)
    {
        var n = Obj[k];
        if (n == null) return "";
        if (n is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s;
            return n.ToJsonString();
        }
        return n.ToJsonString(Json.Options);
    }
}

/// <summary>Bir kontrolün tek maddesi</summary>
public sealed record RuleItem(string Code, string Level, string Title, string Msg, string Ref = "");

public sealed class RuleResult
{
    public string Level { get; set; } = "ok";
    public List<RuleItem> Items { get; } = new();
    public Dictionary<string, object?> Stats { get; set; } = new();
    public long RuleSetId { get; set; }
    public int VersionNo { get; set; }
    public string RuleSetTitle { get; set; } = "";
}

/// <summary>Etkin kural sürümü</summary>
public sealed class RuleSet
{
    public long Id { get; init; }
    public string ProgramType { get; init; } = "";
    public int VersionNo { get; init; }
    public string Title { get; init; } = "";
    public RuleParams Params { get; init; } = new(new JsonObject());
}

/// <summary>
/// Yönerge kural motoru.
///  - Sayısal kurallar koda gömülü DEĞİLDİR; rule_sets tablosunda sürümlü JSON olarak tutulur.
///  - Yalnızca ONAYLI kayıtlar hesaba katılır.
///  - Sonuçlar uyarıdır, karar koordinatöre aittir.
///  - Her kontrolün anlık görüntüsü kullanılan kural sürümüyle birlikte rule_checks'e yazılır.
/// </summary>
public static class Rules
{
    /// <summary>Parametre tanımları: anahtar => (etiket, tür, dayanak)</summary>
    public static readonly Dictionary<string, List<(string Key, string Label, string Kind, string Ref)>> ParamDefs = new()
    {
        ["CAP"] = new()
        {
            ("anadal_gano_esik", "Ana dal GANO uyarı/kayıt silme eşiği", "number", "ÇAP Yönergesi md. 9/e (2,72)"),
            ("anadal_gano_tek_sefer_alt", "Bir defaya mahsus düşülebilecek alt sınır", "number", "ÇAP Yönergesi md. 9/e (2,50)"),
            ("anadal_gano_ihlal_hakki", "Eşik altına düşme hakkı (adet)", "int", "ÇAP Yönergesi md. 9/e (bir defa)"),
            ("mezuniyet_anadal_gano_min", "Diploma için ana dal mezuniyet GANO alt sınırı", "number", "ÇAP Yönergesi md. 8/c (2,72)"),
            ("program_ortalama_min", "ÇAP programı not ortalaması alt sınırı (4'lük)", "number", "Yönetmelik md. 17/4 (100'lük 70)"),
            ("program_ortalama_ihlal_hakki", "ÇAP ortalaması alt sınır altına düşme hakkı (adet)", "int", "Yönetmelik md. 17/4 (bir defaya mahsus)"),
            ("ardisik_ders_almama_limiti", "Üst üste ders almama dönem sınırı", "int", "ÇAP Yönergesi md. 9/d (2 dönem)"),
            ("gerekli_toplam_akts", "Protokole göre tamamlanması gereken AKTS", "number", "Protokol (135 AKTS) / md. 7/2"),
            ("azami_sure_donem", "Azami öğrenim süresi (yarıyıl)", "int", "2547 s. Kanun md. 44/c (4 yıllık: 7 yıl)"),
            ("teknik_secmeli_sayilari", "Yarıyıla göre teknik seçmeli ders sayısı", "json", "Elektrik-Elektronik → Bilgisayar protokolü"),
            ("basvuru_gano_min", "Başvuru için GANO alt sınırı (bilgi)", "number", "ÇAP Yönergesi md. 5/d (2,75)"),
            ("basvuru_yariyil_araligi", "Başvuru yarıyıl aralığı (bilgi)", "text", "ÇAP Yönergesi md. 5/f (3-5)"),
            ("not_olcegi", "Harf notu katsayıları", "json", "Selçuk Ün. Eğitim-Öğretim Yönetmeliği"),
            ("gecer_notlar", "Geçer harf notları", "json", "Selçuk Ün. Eğitim-Öğretim Yönetmeliği"),
            ("kosullu_notlar", "Koşullu geçer harf notları", "json", "Selçuk Ün. Eğitim-Öğretim Yönetmeliği"),
        },
        ["YDP"] = new()
        {
            ("anadal_gano_min", "Ana dal GANO alt sınırı (altında kayıt silinir)", "number", "Yandal Yönergesi md. 7/5 (2,30)"),
            ("program_ortalama_min", "Yandal not ortalaması alt sınırı", "number", "Yandal Yönergesi md. 7/6 (2,00)"),
            ("program_ortalama_ardisik_donem", "Ortalama alt sınır altında kalınabilecek ardışık dönem", "int", "Yandal Yönergesi md. 7/6 (üst üste 2 dönem)"),
            ("ardisik_ders_almama_limiti", "Üst üste ders almama dönem sınırı", "int", "Yandal Yönergesi md. 7/4 (2 dönem)"),
            ("min_akts", "Alınması gereken en az AKTS", "number", "Yandal Yönergesi md. 5/g (30 AKTS)"),
            ("min_ders_sayisi", "Alınması gereken en az ders sayısı", "int", "Yandal Yönergesi md. 5/g (6 ders)"),
            ("program_toplam_akts_min", "Bölümün ilan edeceği yandal programı en az AKTS", "number", "Yandal Yönergesi md. 5/g (60 AKTS)"),
            ("sertifika_ortalama_min", "Sertifika için yandal ortalaması alt sınırı", "number", "Yandal Yönergesi md. 7/8 (2,00)"),
            ("ek_sure_donem", "Ana dal mezuniyeti sonrası ek süre (yarıyıl)", "int", "Yandal Yönergesi md. 7/9 (2 yarıyıl)"),
            ("azami_sure_donem", "Azami süre (yarıyıl, 0 = kontrol yok)", "int", "Yandal Yönergesi md. 7/9"),
            ("basvuru_gano_min", "Başvuru için GANO alt sınırı (bilgi)", "number", "Yandal Yönergesi md. 5/d (2,50)"),
            ("basvuru_yariyil_araligi", "Başvuru yarıyıl aralığı (bilgi)", "text", "Yandal Yönergesi md. 5/ç (3-6)"),
            ("not_olcegi", "Harf notu katsayıları", "json", "Selçuk Ün. Eğitim-Öğretim Yönetmeliği"),
            ("gecer_notlar", "Geçer harf notları", "json", "Selçuk Ün. Eğitim-Öğretim Yönetmeliği"),
            ("kosullu_notlar", "Koşullu geçer harf notları", "json", "Selçuk Ün. Eğitim-Öğretim Yönetmeliği"),
        },
    };

    public static JsonObject Defaults(string type)
    {
        var scale = new JsonObject
        {
            ["AA"] = 4.0m, ["BA"] = 3.5m, ["BB"] = 3.0m, ["CB"] = 2.5m, ["CC"] = 2.0m,
            ["DC"] = 1.5m, ["DD"] = 1.0m, ["FD"] = 0.5m, ["FF"] = 0.0m, ["DZ"] = 0.0m,
        };
        JsonArray Pass() => new("AA", "BA", "BB", "CB", "CC", "DC", "DD");
        JsonArray Cond() => new("DC", "DD");
        if (type == "CAP")
        {
            return new JsonObject
            {
                ["anadal_gano_esik"] = 2.72m,
                ["anadal_gano_tek_sefer_alt"] = 2.50m,
                ["anadal_gano_ihlal_hakki"] = 1,
                ["mezuniyet_anadal_gano_min"] = 2.72m,
                ["program_ortalama_min"] = 2.00m,
                ["program_ortalama_ihlal_hakki"] = 1,
                ["ardisik_ders_almama_limiti"] = 2,
                ["gerekli_toplam_akts"] = 135,
                ["azami_sure_donem"] = 14,
                ["teknik_secmeli_sayilari"] = new JsonObject { ["5"] = 3, ["6"] = 3, ["7"] = 2 },
                ["basvuru_gano_min"] = 2.75m,
                ["basvuru_yariyil_araligi"] = "3-5",
                ["not_olcegi"] = scale,
                ["gecer_notlar"] = Pass(),
                ["kosullu_notlar"] = Cond(),
            };
        }
        return new JsonObject
        {
            ["anadal_gano_min"] = 2.30m,
            ["program_ortalama_min"] = 2.00m,
            ["program_ortalama_ardisik_donem"] = 2,
            ["ardisik_ders_almama_limiti"] = 2,
            ["min_akts"] = 30,
            ["min_ders_sayisi"] = 6,
            ["program_toplam_akts_min"] = 60,
            ["sertifika_ortalama_min"] = 2.00m,
            ["ek_sure_donem"] = 2,
            ["azami_sure_donem"] = 0,
            ["basvuru_gano_min"] = 2.50m,
            ["basvuru_yariyil_araligi"] = "3-6",
            ["not_olcegi"] = scale,
            ["gecer_notlar"] = Pass(),
            ["kosullu_notlar"] = Cond(),
        };
    }

    public static RuleSet ActiveSet(IAppCtx c, string type)
    {
        var key = "ruleset_" + type;
        if (c.Cache.TryGetValue(key, out var cached) && cached is RuleSet rsCached)
        {
            return rsCached;
        }
        var r = c.Db.One("SELECT * FROM rule_sets WHERE program_type = @type AND is_active = 1 ORDER BY version_no DESC LIMIT 1", new { type });
        var merged = Defaults(type);
        if (r != null && JsonNode.Parse(r.S("params")) is JsonObject stored)
        {
            foreach (var kv in stored.ToList())
            {
                merged[kv.Key] = kv.Value?.DeepClone();
            }
        }
        // Tüm değerlerin aynı biçimde (JSON metninden) okunması için yeniden ayrıştır
        var normalized = (JsonObject)JsonNode.Parse(merged.ToJsonString())!;
        var rs = new RuleSet
        {
            Id = r?.L("id") ?? 0,
            ProgramType = type,
            VersionNo = r?.I("version_no") ?? 0,
            Title = r?.S("title") ?? "Varsayılan",
            Params = new RuleParams(normalized),
        };
        c.Cache[key] = rs;
        return rs;
    }

    public static void ClearCache(IAppCtx c)
    {
        c.Cache.Remove("ruleset_CAP");
        c.Cache.Remove("ruleset_YDP");
    }

    public static Dictionary<string, decimal> GradeScale(IAppCtx c, string type) => ActiveSet(c, type).Params.Scale;

    // ------------------------------------------------------------------

    public static RuleResult Evaluate(IAppCtx c, long studentId, RuleSet? ruleSet = null)
    {
        var s = c.Db.One("SELECT * FROM students WHERE id = @id", new { id = studentId }) ?? throw new InvalidOperationException("Öğrenci bulunamadı");
        var type = s.S("program_type");
        var rs = ruleSet ?? ActiveSet(c, type);
        var p = rs.Params;
        var prog = Progress.Compute(c, studentId, p);
        var res = new RuleResult { RuleSetId = rs.Id, VersionNo = rs.VersionNo, RuleSetTitle = rs.Title };
        void Add(string code, string level, string title, string msg, string reff = "") => res.Items.Add(new RuleItem(code, level, title, msg, reff));

        var current = Terms.Current(c);
        var admission = Terms.Find(c, s.L("admission_term_id"));
        var isCap = type == "CAP";

        if (s.S("status") != "aktif")
        {
            Add("DURUM", "bilgi", "Öğrenci durumu", "Öğrencinin durumu \"" + L.Get(L.StudentStatus, s.S("status")) + "\". Kontroller bilgi amaçlıdır.");
        }

        // --- 1. Ardışık ders almama ---
        var limit = p.I("ardisik_ders_almama_limiti");
        if (current != null && admission != null && limit > 0)
        {
            var terms = c.Db.All("SELECT id, academic_year, season, sort_key FROM terms WHERE season IN ('Güz','Bahar') AND sort_key >= @a AND sort_key <= @b ORDER BY sort_key",
                new { a = admission.I("sort_key"), b = current.I("sort_key") });
            var taken = c.Db.All("SELECT DISTINCT term_id FROM enrollments WHERE student_id = @sid AND approval_state = 'onaylandi' AND status NOT IN ('cikarildi','planlandi')", new { sid = studentId })
                .Select(r => r.L("term_id")).ToHashSet();
            var frozen = c.Db.All("SELECT term_id FROM decisions WHERE student_id = @sid AND decision_type = 'kayit_dondurma' AND state = 'gecerli' AND term_id IS NOT NULL", new { sid = studentId })
                .Select(r => r.L("term_id")).ToHashSet();
            int run = 0, maxRun = 0;
            var runTerms = new List<string>();
            var worst = new List<string>();
            foreach (var t in terms)
            {
                var tid = t.L("id");
                if (tid == current.L("id"))
                {
                    break;
                }
                if (frozen.Contains(tid))
                {
                    continue; // mazeretli/dondurulmuş dönem sayılmaz
                }
                if (taken.Contains(tid))
                {
                    run = 0;
                    runTerms = new List<string>();
                }
                else
                {
                    run++;
                    runTerms.Add(t.S("academic_year") + " " + t.S("season"));
                    if (run > maxRun)
                    {
                        maxRun = run;
                        worst = new List<string>(runTerms);
                    }
                }
            }
            var currentTaken = taken.Contains(current.L("id"));
            var refText = isCap ? "ÇAP Yön. 9/d" : "YDP Yön. 7/4";
            if (maxRun >= limit)
            {
                Add("DERS_ALMAMA", "kritik", "Üst üste ders almama", $"Öğrenci üst üste {maxRun} dönem ders almamış ({string.Join(", ", worst)}). Sınır: {limit} dönem. Kayıt silme koşulu oluşmuş olabilir.", refText);
            }
            else if (run == limit - 1 && run > 0 && !currentTaken && !frozen.Contains(current.L("id")))
            {
                Add("DERS_ALMAMA", "uyari", "Üst üste ders almama riski", $"Öğrenci son {run} dönemde ders almamış ve içinde bulunulan dönem için onaylı ders kaydı yok. Bu dönem de ders almazsa kayıt silme koşulu oluşur.", refText);
            }
            else
            {
                Add("DERS_ALMAMA", "ok", "Dönemlik ders alma", "Üst üste ders almama sınırı aşılmamış.");
            }
        }

        // --- 2. Ana dal GANO ---
        var ganos = c.Db.All(@"SELECT g.gano, g.source, t.academic_year, t.season, t.sort_key FROM gano_records g JOIN terms t ON t.id = g.term_id
                               WHERE g.student_id = @sid AND g.state = 'gecerli' ORDER BY t.sort_key, g.id", new { sid = studentId });
        decimal? latestGano = ganos.Count > 0 ? ganos[^1].Dec("gano") : null;
        if (ganos.Count == 0)
        {
            Add("GANO", "bilgi", "Ana dal GANO", "Koordinatör tarafından girilmiş ana dal GANO kaydı yok. GANO eşikleri kontrol edilemedi.");
        }
        else if (isCap)
        {
            var esik = p.N("anadal_gano_esik");
            var alt = p.N("anadal_gano_tek_sefer_alt");
            var hak = p.I("anadal_gano_ihlal_hakki");
            // aynı dönem için yalnızca son kayıt dikkate alınır
            var byTerm = new SortedDictionary<int, Row>();
            foreach (var g in ganos)
            {
                byTerm[g.I("sort_key")] = g;
            }
            var below = byTerm.Values.Where(g => g.Dec("gano") < esik).ToList();
            var belowAlt = byTerm.Values.Where(g => g.Dec("gano") < alt).ToList();
            var txt = string.Join(", ", below.Select(g => g.S("academic_year") + " " + g.S("season") + ": " + H.Num(g.Dec("gano"))));
            if (below.Count > hak)
            {
                Add("GANO", "kritik", "Ana dal GANO eşiği", $"Ana dal GANO {below.Count} kez {H.Num(esik)} altına düşmüş ({txt}). Yönergeye göre kayıt silme koşulu oluşmuş olabilir.", "ÇAP Yön. 9/e");
            }
            else if (below.Count > 0)
            {
                Add("GANO", "uyari", "Ana dal GANO eşiği", $"Ana dal GANO bir kez {H.Num(esik)} altına düşmüş ({txt})" + (belowAlt.Count > 0 ? " (" + H.Num(alt) + " altında)" : "") + ". Bir defalık hak kullanıldı; tekrar " + H.Num(esik) + " altına düşerse kayıt silinir.", "ÇAP Yön. 9/e");
            }
            else
            {
                Add("GANO", "ok", "Ana dal GANO", $"Son GANO {H.Num(latestGano)}; eşik {H.Num(esik)} sağlanıyor.");
            }
        }
        else
        {
            var min = p.N("anadal_gano_min");
            if (latestGano < min)
            {
                Add("GANO", "kritik", "Ana dal GANO alt sınırı", $"Son ana dal GANO {H.Num(latestGano)}, alt sınır {H.Num(min)}. Yönergeye göre yandal kaydı silinir.", "YDP Yön. 7/5");
            }
            else
            {
                Add("GANO", "ok", "Ana dal GANO", $"Son GANO {H.Num(latestGano)}; alt sınır {H.Num(min)} sağlanıyor.");
            }
        }

        // --- 3. Program not ortalaması ---
        var pmin = p.N("program_ortalama_min");
        if (prog.Gpa == null)
        {
            Add("ORTALAMA", "bilgi", "Program not ortalaması", "Henüz onaylanmış notlu ders yok.");
        }
        else if (!isCap)
        {
            var consec = 0;
            for (var i = prog.TermCumulative.Count - 1; i >= 0; i--)
            {
                if (prog.TermCumulative[i].Avg < pmin) consec++;
                else break;
            }
            var lim = p.I("program_ortalama_ardisik_donem");
            if (consec >= lim)
            {
                Add("ORTALAMA", "kritik", "Yandal ortalaması", $"Yandal not ortalaması üst üste {consec} dönem {H.Num(pmin)} altında. Yönergeye göre kayıt silinir.", "YDP Yön. 7/6");
            }
            else if (consec > 0)
            {
                Add("ORTALAMA", "uyari", "Yandal ortalaması", $"Yandal not ortalaması ({H.Num(prog.Gpa)}) {H.Num(pmin)} altında. Bir sonraki dönem de altında kalırsa kayıt silinir.", "YDP Yön. 7/6");
            }
            else
            {
                Add("ORTALAMA", "ok", "Yandal ortalaması", $"Yandal not ortalaması {H.Num(prog.Gpa)}.");
            }
        }
        else
        {
            var belowCount = prog.TermCumulative.Count(t => t.Avg < pmin);
            var hak = p.I("program_ortalama_ihlal_hakki");
            if (belowCount > hak)
            {
                Add("ORTALAMA", "kritik", "ÇAP ortalaması", $"ÇAP not ortalaması {belowCount} dönem sonunda {H.Num(pmin)} altında kalmış. Kayıt silme koşulu oluşmuş olabilir.", "Yönetmelik 17/4");
            }
            else if (belowCount > 0)
            {
                Add("ORTALAMA", "uyari", "ÇAP ortalaması", $"ÇAP not ortalaması bir dönem sonunda {H.Num(pmin)} altına düşmüş (bir defalık hak). Güncel ortalama: {H.Num(prog.Gpa)}.", "Yönetmelik 17/4");
            }
            else
            {
                Add("ORTALAMA", "ok", "ÇAP ortalaması", $"ÇAP not ortalaması {H.Num(prog.Gpa)}.");
            }
        }
        if (prog.ConditionalCount > 0 && prog.Gpa != null && prog.Gpa < 2.0m)
        {
            Add("KOSULLU", "uyari", "Koşullu geçer notlar", $"{prog.ConditionalCount} ders koşullu geçer notla (DC/DD) geçilmiş, ortalama 2,00 altında. Bu dersler başarısız sayılabilir.", "Eğitim-Öğretim Yön.");
        }

        // --- 4. Ders listesi ---
        if (isCap)
        {
            var need = p.N("gerekli_toplam_akts");
            if (prog.PlanEcts < need)
            {
                Add("PLAN", "uyari", "Ders listesi eksik", $"Onaylı ders listesindeki toplam AKTS {H.Ects(prog.PlanEcts)}, protokol gereği {H.Ects(need)} AKTS olmalı.", "Protokol");
            }
            foreach (var kv in p.Map("teknik_secmeli_sayilari"))
            {
                var sem = int.TryParse(kv.Key, out var sm) ? sm : 0;
                var have = prog.ElectivesBySem.TryGetValue(sem, out var hv) ? hv : 0;
                if (have < (int)kv.Value)
                {
                    Add("SECMELI_" + kv.Key, "uyari", $"{kv.Key}. yarıyıl teknik seçmeli", $"Ders listesinde {kv.Key}. yarıyıl teknik seçmeli ders sayısı {have}, protokol gereği {(int)kv.Value} olmalı.", "Protokol");
                }
            }
        }
        else if (prog.PlanCount < p.I("min_ders_sayisi") || prog.PlanEcts < p.N("min_akts"))
        {
            Add("PLAN", "uyari", "Ders listesi yönerge alt sınırının altında", $"Onaylı ders listesi: {prog.PlanCount} ders / {H.Ects(prog.PlanEcts)} AKTS. En az {p.I("min_ders_sayisi")} ders ve {H.Ects(p.N("min_akts"))} AKTS olmalı.", "YDP Yön. 5/g");
        }

        // --- 5. Azami süre ---
        var maxTerms = p.I("azami_sure_donem");
        if (maxTerms > 0 && current != null && admission != null)
        {
            var elapsed = c.Db.Count("SELECT COUNT(*) FROM terms WHERE season IN ('Güz','Bahar') AND sort_key >= @a AND sort_key <= @b",
                new { a = admission.I("sort_key"), b = current.I("sort_key") });
            if (elapsed > maxTerms)
            {
                Add("SURE", "kritik", "Azami süre", $"Öğrenci {elapsed}. yarıyılında; azami süre {maxTerms} yarıyıl aşılmış.", "2547 s. K. 44/c");
            }
            else if (elapsed >= maxTerms - 1)
            {
                Add("SURE", "uyari", "Azami süre yaklaşıyor", $"Öğrenci {elapsed}. yarıyılında; azami süre {maxTerms} yarıyıl.", "2547 s. K. 44/c");
            }
        }

        // --- 6. Eksik transkript / girilmemiş not ---
        if (current != null)
        {
            var rows = c.Db.All(@"SELECT DISTINCT e.term_id, t.academic_year, t.season FROM enrollments e JOIN terms t ON t.id = e.term_id
                                  WHERE e.student_id = @sid AND e.approval_state = 'onaylandi' AND e.status NOT IN ('cikarildi','planlandi','muaf') AND e.is_legacy = 0 AND t.sort_key < @k",
                new { sid = studentId, k = current.I("sort_key") });
            foreach (var r in rows)
            {
                var has = c.Db.Count("SELECT COUNT(*) FROM documents WHERE student_id = @sid AND doc_type = 'cap_ydp_transkript' AND term_id = @tid AND state IN ('bekliyor','onaylandi')",
                    new { sid = studentId, tid = r.L("term_id") });
                if (has == 0)
                {
                    Add("TRANSKRIPT_" + r.L("term_id"), "uyari", "Eksik transkript", r.S("academic_year") + " " + r.S("season") + " dönemi için ÇAP/YDP not belgesi (transkript) yüklenmemiş.", "Koordinatör talimatı");
                }
            }
            var ungraded = c.Db.Count(@"SELECT COUNT(*) FROM enrollments e JOIN terms t ON t.id = e.term_id
                                        WHERE e.student_id = @sid AND e.approval_state = 'onaylandi' AND e.status = 'alindi' AND t.sort_key < @k",
                new { sid = studentId, k = current.I("sort_key") });
            if (ungraded > 0)
            {
                Add("NOT_EKSIK", "uyari", "Notu girilmemiş dersler", $"Geçmiş dönemlere ait {ungraded} dersin notu henüz onaylanmamış.");
            }
        }

        // --- 7. Tamamlama ---
        var complete = prog.PlanCount > 0 && prog.RemainingCount == 0;
        if (isCap)
        {
            complete = complete && prog.CompletedEcts >= p.N("gerekli_toplam_akts");
            if (complete)
            {
                var ganoOk = latestGano != null && latestGano >= p.N("mezuniyet_anadal_gano_min");
                var avgOk = prog.Gpa != null && prog.Gpa >= p.N("program_ortalama_min");
                if (ganoOk && avgOk)
                {
                    Add("MEZUNIYET", "bilgi", "Mezuniyet koşulları", "Tüm protokol dersleri tamamlanmış, GANO ve ortalama şartları sağlanıyor. Ana dal mezuniyeti teyit edildikten sonra mezuniyet kararı verilebilir.", "ÇAP Yön. 8/c-ç");
                }
                else
                {
                    Add("MEZUNIYET", "uyari", "Mezuniyet koşulları", "Dersler tamamlanmış ancak " + (!ganoOk ? "ana dal GANO " + H.Num(p.N("mezuniyet_anadal_gano_min")) + " şartı " : "") + (!avgOk ? "ÇAP ortalama şartı " : "") + "sağlanmıyor.", "ÇAP Yön. 8/c");
                }
            }
        }
        else if (complete && prog.PlanCount >= p.I("min_ders_sayisi"))
        {
            var avgOk = prog.Gpa != null && prog.Gpa >= p.N("sertifika_ortalama_min");
            Add("SERTIFIKA", avgOk ? "bilgi" : "uyari", "Sertifika koşulları",
                avgOk ? "Yandal dersleri tamamlanmış ve ortalama şartı sağlanıyor. Ana dal mezuniyeti sonrası sertifika kararı verilebilir."
                      : "Dersler tamamlanmış ancak yandal ortalaması " + H.Num(p.N("sertifika_ortalama_min")) + " altında.", "YDP Yön. 7/8");
        }

        var order = new Dictionary<string, int> { ["ok"] = 0, ["bilgi"] = 1, ["uyari"] = 2, ["kritik"] = 3 };
        var sorted = res.Items.OrderByDescending(i => order[i.Level]).ToList();
        res.Items.Clear();
        res.Items.AddRange(sorted);
        res.Level = sorted.Count == 0 ? "ok" : sorted.Max(i => order[i.Level]) switch { 3 => "kritik", 2 => "uyari", 1 => "bilgi", _ => "ok" };
        res.Stats = new Dictionary<string, object?>
        {
            ["plan_count"] = prog.PlanCount,
            ["plan_ects"] = prog.PlanEcts,
            ["completed_count"] = prog.CompletedCount,
            ["completed_ects"] = prog.CompletedEcts,
            ["remaining_count"] = prog.RemainingCount,
            ["remaining_ects"] = prog.RemainingEcts,
            ["gpa"] = prog.Gpa,
            ["latest_gano"] = latestGano,
        };
        return res;
    }

    /// <summary>
    /// Kontrolü çalıştırır ve sonucu değiştirilemez şekilde kaydeder.
    /// Uyarı/kritik durum değiştiyse öğrenciye ve koordinatörlere bildirim gönderir.
    /// </summary>
    public static RuleResult Snapshot(IAppCtx c, long studentId, string evt, bool notify = true)
    {
        var s = c.Db.One("SELECT id, program_type, full_name, student_no FROM students WHERE id = @id", new { id = studentId })!;
        var rs = ActiveSet(c, s.S("program_type"));
        var res = Evaluate(c, studentId, rs);
        var warn = res.Items.Where(i => i.Level is "uyari" or "kritik").ToList();
        var sig = Json.Sha256Hex(string.Join("|", warn.Select(i => i.Code + i.Level)));
        var last = c.Db.One("SELECT results FROM rule_checks WHERE student_id = @sid ORDER BY id DESC LIMIT 1", new { sid = studentId });
        var lastSig = "";
        if (last != null && JsonNode.Parse(last.S("results")) is JsonObject lo && lo["signature"] is JsonValue sv && sv.TryGetValue<string>(out var ls))
        {
            lastSig = ls;
        }
        if (rs.Id > 0)
        {
            var payload = new JsonObject
            {
                ["signature"] = sig,
                ["items"] = new JsonArray(res.Items.Select(i => (JsonNode)new JsonObject
                {
                    ["code"] = i.Code, ["level"] = i.Level, ["title"] = i.Title, ["msg"] = i.Msg, ["ref"] = i.Ref,
                }).ToArray()),
                ["stats"] = JsonNode.Parse(Json.Encode(res.Stats)),
                ["params"] = rs.Params.Obj.DeepClone(),
            };
            c.Db.Insert("rule_checks", new Data
            {
                ["student_id"] = studentId,
                ["rule_set_id"] = rs.Id,
                ["trigger_event"] = evt.Length > 100 ? evt[..100] : evt,
                ["summary_level"] = res.Level,
                ["results"] = payload.ToJsonString(Json.Options),
                ["run_by"] = c.User?.Id,
                ["created_at"] = Clock.Now(),
            });
        }
        if (notify && warn.Count > 0 && sig != lastSig)
        {
            var msg = "Kural kontrolü: " + warn.Count + " uyarı (" + string.Join(", ", warn.Select(i => i.Title).Distinct()) + ")";
            Notify.Student(c, studentId, msg, "/ogrenci");
            Notify.Coordinators(c, s.S("full_name") + " (" + s.S("student_no") + ") — " + msg, "/koordinator/ogrenci/" + studentId);
        }
        return res;
    }

    /// <summary>Kaydedilmiş kontrolün maddelerini okur</summary>
    public static List<RuleItem> ItemsOf(string resultsJson)
    {
        var list = new List<RuleItem>();
        if (JsonNode.Parse(resultsJson) is JsonObject o && o["items"] is JsonArray a)
        {
            foreach (var n in a)
            {
                if (n is JsonObject i)
                {
                    list.Add(new RuleItem(Str(i["code"]), Str(i["level"]), Str(i["title"]), Str(i["msg"]), Str(i["ref"])));
                }
            }
        }
        return list;

        static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    }
}
