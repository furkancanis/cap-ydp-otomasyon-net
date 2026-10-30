using System.Globalization;
using System.Text;
using CapYdp.Services;
using Microsoft.AspNetCore.Html;

namespace CapYdp.Infrastructure;

/// <summary>Görünüm yardımcıları: kaçış, biçimlendirme, rozet, küçük formlar.</summary>
public static class H
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>HTML kaçışı (XSS koruması). Türkçe karakterler olduğu gibi kalır.</summary>
    public static string E(object? v)
    {
        var s = v switch
        {
            null => "",
            string str => str,
            _ => Convert.ToString(v, Inv) ?? ""
        };
        var sb = new StringBuilder(s.Length + 16);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&#39;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    public static HtmlString Raw(string s) => new(s);

    // ---------------- biçimlendirme ----------------

    public static string Dt(DateTime? d) => d == null ? "—" : d.Value.ToString("dd.MM.yyyy HH:mm", Inv);

    public static string Dt(string? s) => DateTime.TryParse(s, Inv, DateTimeStyles.None, out var d) ? Dt(d) : (string.IsNullOrEmpty(s) ? "—" : s);

    public static string D(DateTime? d) => d == null ? "—" : d.Value.ToString("dd.MM.yyyy", Inv);

    /// <summary>2,72 biçiminde (Türkçe ondalık virgül)</summary>
    public static string Num(decimal? v, int dec = 2)
    {
        if (v == null)
        {
            return "—";
        }
        var s = Math.Round(v.Value, dec, MidpointRounding.AwayFromZero).ToString("N" + dec, Inv);
        // 1,234.56 -> 1.234,56
        return s.Replace(",", "\u0001").Replace(".", ",").Replace("\u0001", ".");
    }

    public static string Ects(decimal? v)
    {
        if (v == null)
        {
            return "—";
        }
        var x = v.Value;
        return x == Math.Floor(x) ? ((long)x).ToString(Inv) : x.ToString("0.0", Inv).Replace('.', ',');
    }

    public static string Size(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes + " B";
        }
        if (bytes < 1048576)
        {
            return (bytes / 1024.0).ToString("0.0", Inv).Replace('.', ',') + " KB";
        }
        return (bytes / 1048576.0).ToString("0.0", Inv).Replace('.', ',') + " MB";
    }

    public static string Initials(string name)
    {
        var p = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 0)
        {
            return "?";
        }
        var a = p[0][..1];
        var b = p.Length > 1 ? p[^1][..1] : "";
        return TrUpper(a + b);
    }

    /// <summary>Türkçe büyük harf (i → İ, ı → I)</summary>
    public static string TrUpper(string s) => s.Replace('i', 'İ').Replace('ı', 'I').ToUpperInvariant();

    /// <summary>Türkçe küçük harf (İ → i, I → ı)</summary>
    public static string TrLower(string s) => s.Replace('İ', 'i').Replace('I', 'ı').ToLowerInvariant();

    private static void Unused()
    {
    }

    // ---------------- rozet ----------------

    public static HtmlString Badge(string? state, string? label = null)
    {
        var cls = state switch
        {
            "onaylandi" or "gecti" or "aktif" or "ok" or "gecerli" or "eklendi" or "mezun" or "sertifika" or "muaf" => "b-green",
            "onay_bekliyor" or "bekliyor" or "yuklendi" or "uyari" or "planlandi" or "alindi" or "donduruldu" => "b-amber",
            "reddedildi" or "kaldi" or "kritik" or "kaydi_silindi" => "b-red",
            "iptal" or "cikarildi" or "ayrildi" => "b-gray",
            _ => "b-blue",
        };
        label ??= L.Any(state);
        return new HtmlString($"<span class=\"badge {cls}\">{E(label)}</span>");
    }

    // ---------------- seçim listeleri ----------------

    public static HtmlString Options(IEnumerable<KeyValuePair<string, string>> map, string? selected = null, bool includeEmpty = false)
    {
        var sb = new StringBuilder();
        if (includeEmpty)
        {
            sb.Append("<option value=\"\">— Seçiniz —</option>");
        }
        foreach (var kv in map)
        {
            sb.Append("<option value=\"").Append(E(kv.Key)).Append('"');
            if (kv.Key == selected)
            {
                sb.Append(" selected");
            }
            sb.Append('>').Append(E(kv.Value)).Append("</option>");
        }
        return new HtmlString(sb.ToString());
    }

    public static HtmlString TermOptions(IEnumerable<Row> terms, long? selected = null, bool includeEmpty = false)
    {
        var sb = new StringBuilder();
        if (includeEmpty)
        {
            sb.Append("<option value=\"\">— Seçiniz —</option>");
        }
        foreach (var t in terms)
        {
            sb.Append("<option value=\"").Append(t.L("id")).Append('"');
            if (selected != null && t.L("id") == selected)
            {
                sb.Append(" selected");
            }
            sb.Append('>').Append(E(t.S("academic_year") + " " + t.S("season") + (t.B("is_current") ? " (aktif dönem)" : ""))).Append("</option>");
        }
        return new HtmlString(sb.ToString());
    }

    // ---------------- küçük formlar ----------------

    public static string CsrfField(RequestCtx c) => "<input type=\"hidden\" name=\"_csrf\" value=\"" + E(c.CsrfToken()) + "\">";

    /// <summary>Tek butonluk POST formu</summary>
    public static HtmlString PostBtn(RequestCtx c, string label, string action, IDictionary<string, string>? hidden = null, string cls = "", string confirm = "")
    {
        var sb = new StringBuilder();
        sb.Append("<form method=\"post\" action=\"").Append(E(action)).Append("\" class=\"inline\"");
        if (confirm != "")
        {
            sb.Append(" data-confirm=\"").Append(E(confirm)).Append('"');
        }
        sb.Append('>').Append(CsrfField(c));
        if (hidden != null)
        {
            foreach (var kv in hidden)
            {
                sb.Append("<input type=\"hidden\" name=\"").Append(E(kv.Key)).Append("\" value=\"").Append(E(kv.Value)).Append("\">");
            }
        }
        sb.Append("<button class=\"btn sm ").Append(E(cls)).Append("\" type=\"submit\">").Append(E(label)).Append("</button></form>");
        return new HtmlString(sb.ToString());
    }

    /// <summary>Gerekçe isteyen açılır form (ret, iptal, çıkarma vb.)</summary>
    public static HtmlString ReasonPop(RequestCtx c, string btnLabel, string action, IDictionary<string, string>? hidden = null, string btnClass = "danger", string submit = "Kaydet", string hint = "Gerekçe (zorunlu)")
    {
        var sb = new StringBuilder();
        sb.Append("<details class=\"pop\"><summary class=\"btn sm ").Append(E(btnClass)).Append("\">").Append(E(btnLabel)).Append("</summary><div class=\"pop-body\">");
        sb.Append("<form method=\"post\" action=\"").Append(E(action)).Append("\">").Append(CsrfField(c));
        if (hidden != null)
        {
            foreach (var kv in hidden)
            {
                sb.Append("<input type=\"hidden\" name=\"").Append(E(kv.Key)).Append("\" value=\"").Append(E(kv.Value)).Append("\">");
            }
        }
        sb.Append("<label>").Append(E(hint)).Append("</label><textarea name=\"reason\" maxlength=\"500\" required minlength=\"5\"></textarea>");
        sb.Append("<button class=\"btn sm ").Append(btnClass == "danger" ? "danger" : "").Append("\" type=\"submit\">").Append(E(submit)).Append("</button></form></div></details>");
        return new HtmlString(sb.ToString());
    }

    public static Dictionary<string, string> KV(params string[] pairs)
    {
        var d = new Dictionary<string, string>();
        for (var i = 0; i + 1 < pairs.Length; i += 2)
        {
            d[pairs[i]] = pairs[i + 1];
        }
        return d;
    }

    public static HtmlString DocLink(long id, string label) =>
        new($"<a href=\"/belge/{id}\" target=\"_blank\" rel=\"noopener\">{E(label)}</a>");

    /// <summary>Sorgu dizesi oluşturur</summary>
    public static string Url(string path, params (string Key, object? Value)[] q)
    {
        var parts = q.Where(x => x.Value != null && Convert.ToString(x.Value, Inv) != "")
            .Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(Convert.ToString(x.Value, Inv)!));
        var qs = string.Join("&", parts);
        return qs == "" ? path : path + "?" + qs;
    }

    private static readonly Dictionary<string, string> AssetVersions = new();

    public static string Asset(string webRoot, string name)
    {
        lock (AssetVersions)
        {
            if (!AssetVersions.TryGetValue(name, out var v))
            {
                var f = Path.Combine(webRoot, "assets", name);
                v = File.Exists(f) ? File.GetLastWriteTimeUtc(f).Ticks.ToString(Inv) : "1";
                AssetVersions[name] = v;
            }
            return "/assets/" + name + "?v=" + v;
        }
    }
}

/// <summary>Menü öğesi (Icon: wwwroot ikon setindeki simge adı)</summary>
public sealed record NavItem(string Href, string Label, string Icon, long Count = 0, bool Exact = false, string Desc = "");

/// <summary>Menü grubu</summary>
public sealed record NavGroup(string Name, string Icon, List<NavItem> Items)
{
    public long Total => Items.Sum(i => i.Count);
}

/// <summary>Role göre üst menü ve hızlı erişim kutuları</summary>
public static class Nav
{
    public static List<NavGroup> Build(RequestCtx c)
    {
        var u = c.User;
        var nav = new List<NavGroup>();
        if (u == null)
        {
            return nav;
        }
        if (c.Cache.TryGetValue("nav", out var cached) && cached is List<NavGroup> l)
        {
            return l;
        }
        if (u.Role == "koordinator")
        {
            var pendingAll = c.Db.Count("SELECT (SELECT COUNT(*) FROM student_courses WHERE state='onay_bekliyor') + (SELECT COUNT(*) FROM enrollments WHERE approval_state='onay_bekliyor') + (SELECT COUNT(*) FROM grade_submissions WHERE state='onay_bekliyor') + (SELECT COUNT(*) FROM documents WHERE state='bekliyor')");
            var reqs = c.Db.Count("SELECT COUNT(*) FROM course_requests WHERE state='bekliyor'");
            nav.Add(new NavGroup("Öğrenci İşleri", "users", new List<NavItem>
            {
                new("/koordinator/onay", "Onay Bekleyenler", "check", pendingAll, false, "Ders, not ve belge onayları"),
                new("/koordinator/ogrenciler", "Öğrenciler", "users", 0, false, "ÇAP / YDP öğrenci listesi ve kartları"),
                new("/koordinator/ogrenci/yeni", "Öğrenci Ekle / İçe Aktar", "user-plus", 0, false, "Tek tek veya CSV ile"),
            }));
            nav.Add(new NavGroup("Tanımlar", "book", new List<NavItem>
            {
                new("/koordinator/katalog", "Ders Kataloğu", "book", 0, false, "Protokol dersleri ve AKTS"),
                new("/koordinator/ders-talepleri", "Katalog Dışı Talepler", "inbox", reqs, false, "Öğrencilerden gelen talepler"),
                new("/koordinator/donemler", "Akademik Dönemler", "calendar", 0, false, "Aktif dönem seçimi"),
                new("/koordinator/kurallar", "Yönerge Kuralları", "scale", 0, false, "Sürümlü kural setleri"),
            }));
            nav.Add(new NavGroup("Sistem", "sliders", new List<NavItem>
            {
                new("/koordinator/kullanicilar", "Kullanıcı Hesapları", "key", 0, false, "Hesap açma ve şifre sıfırlama"),
                new("/koordinator/islem-kaydi", "İşlem Kaydı (Denetim)", "list", 0, false, "Değiştirilemez işlem geçmişi"),
                new("/koordinator/ayarlar", "Sistem Ayarları", "sliders", 0, false, "Bölüm ve dosya ayarları"),
            }));
        }
        else if (u.Role == "bolum_baskani")
        {
            nav.Add(new NavGroup("İzleme", "chart", new List<NavItem>
            {
                new("/koordinator/ogrenciler", "Öğrenciler", "users", 0, false, "Tüm ÇAP / YDP öğrencileri"),
                new("/koordinator/islem-kaydi", "İşlem Kaydı (Denetim)", "list", 0, false, "Kim, ne zaman, ne yaptı"),
            }));
            nav.Add(new NavGroup("Tanımlar", "book", new List<NavItem>
            {
                new("/koordinator/katalog", "Ders Kataloğu", "book", 0, false, "Protokol dersleri"),
                new("/koordinator/ders-talepleri", "Ders Talepleri", "inbox", 0, false, "Katalog dışı talepler"),
                new("/koordinator/donemler", "Akademik Dönemler", "calendar", 0, false, "Dönem listesi"),
                new("/koordinator/kurallar", "Yönerge Kuralları", "scale", 0, false, "Kural sürümleri"),
            }));
        }
        else
        {
            var sid = u.StudentId ?? 0;
            var msgs = c.Db.Count("SELECT COUNT(*) FROM messages WHERE student_id = @sid AND sender_role <> 'ogrenci' AND read_at IS NULL", new { sid });
            var reqs = c.Db.Count("SELECT COUNT(*) FROM document_requests WHERE student_id = @sid AND state IN ('bekliyor','reddedildi')", new { sid });
            nav.Add(new NavGroup("Öğrenim", "book", new List<NavItem>
            {
                new("/ogrenci/dersler", "Derslerim", "book", 0, false, "Ders listesi ve dönem dersleri"),
                new("/ogrenci/notlar", "Dönem Sonu Not Girişi", "award", 0, false, "Transkript ve not girişi"),
            }));
            nav.Add(new NavGroup("Belge ve İletişim", "file", new List<NavItem>
            {
                new("/ogrenci/belgeler", "Belgelerim", "file", reqs, false, "Form, transkript ve istenen belgeler"),
                new("/ogrenci/mesajlar", "Mesajlar", "chat", msgs, false, "Koordinatörle yazışma"),
            }));
        }
        c.Cache["nav"] = nav;
        return nav;
    }

    public static string Home(Actor u) => u.Role switch
    {
        "koordinator" => "/koordinator",
        "bolum_baskani" => "/baskan",
        _ => "/ogrenci",
    };

    /// <summary>İkon setinden bir simge (wwwroot/assets/icons.svg değil, sayfa içi sprite kullanılır)</summary>
    public static HtmlString Icon(string name, string cls = "ic") =>
        new($"<svg class=\"{cls}\" aria-hidden=\"true\" focusable=\"false\"><use href=\"#i-{H.E(name)}\"></use></svg>");
}

/// <summary>İşlem kaydı tablosu kısmi görünüm modeli</summary>
public sealed record AuditTableModel(List<Row> Logs, bool ShowStudent = false);
