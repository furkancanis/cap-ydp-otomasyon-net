namespace CapYdp.Services;

/// <summary>Sabit etiketler (durum, rol, belge türü ...)</summary>
public static class L
{
    public static readonly Dictionary<string, string> Program = new() { ["CAP"] = "Çift Anadal (ÇAP)", ["YDP"] = "Yandal (YDP)" };
    public static readonly Dictionary<string, string> ProgramShort = new() { ["CAP"] = "ÇAP", ["YDP"] = "YDP" };
    public static readonly Dictionary<string, string> Role = new() { ["koordinator"] = "Koordinatör", ["ogrenci"] = "Öğrenci", ["bolum_baskani"] = "Bölüm Başkanı" };

    public static readonly Dictionary<string, string> StudentStatus = new()
    {
        ["aktif"] = "Aktif", ["donduruldu"] = "Kaydı Donduruldu", ["mezun"] = "Mezun (ÇAP Diploması)",
        ["sertifika"] = "Sertifika Aldı", ["kaydi_silindi"] = "Kaydı Silindi", ["ayrildi"] = "Kendi İsteğiyle Ayrıldı",
    };

    public static readonly Dictionary<string, string> Approval = new()
    {
        ["onay_bekliyor"] = "Onay bekliyor", ["onaylandi"] = "Onaylandı", ["reddedildi"] = "Reddedildi",
        ["iptal"] = "İptal", ["cikarildi"] = "Listeden çıkarıldı",
        ["bekliyor"] = "Bekliyor", ["yuklendi"] = "Yüklendi", ["eklendi"] = "Kataloğa eklendi",
        ["gecerli"] = "Geçerli", ["aktif"] = "Aktif",
    };

    public static readonly Dictionary<string, string> EnrStatus = new()
    {
        ["planlandi"] = "Planlandı", ["alindi"] = "Alındı", ["gecti"] = "Geçti", ["kaldi"] = "Kaldı",
        ["muaf"] = "Muaf", ["cikarildi"] = "Çıkarıldı (bırakıldı)",
    };

    public static readonly Dictionary<string, string> Category = new()
    {
        ["zorunlu"] = "Zorunlu", ["teknik_secmeli"] = "Teknik Seçmeli", ["staj"] = "Staj / İşletmede Eğitim", ["diger"] = "Diğer",
    };

    public static readonly Dictionary<string, string> Scope = new() { ["HER_IKISI"] = "ÇAP ve YDP", ["CAP"] = "Yalnız ÇAP", ["YDP"] = "Yalnız YDP" };

    public static readonly Dictionary<string, string> DocTypes = new()
    {
        ["kabul_protokol_formu"] = "Kabul ve Ders Protokol Formu (imzalı)",
        ["ders_kayit_formu"] = "Ders Kayıt Formu (imzalı)",
        ["cap_ydp_transkript"] = "ÇAP/YDP Not Belgesi (Transkript)",
        ["anadal_transkript"] = "Ana Dal Transkripti",
        ["yonetim_kurulu_karari"] = "Yönetim Kurulu Kararı / Resmi Yazı",
        ["muafiyet_formu"] = "Özel Şartlı Derslerin Muafiyeti Formu",
        ["dilekce"] = "Dilekçe",
        ["mesaj_eki"] = "Mesaj Eki",
        ["diger"] = "Diğer Belge",
    };

    public static readonly Dictionary<string, string> Decision = new()
    {
        ["mezuniyet"] = "Mezuniyet (ÇAP diploması)", ["sertifika"] = "Yandal sertifikası",
        ["kayit_silme"] = "Kayıt silme", ["kayit_dondurma"] = "Kayıt dondurma",
        ["dondurma_kaldirma"] = "Dondurmayı kaldırma (aktifleştirme)", ["ayrilma"] = "Kendi isteğiyle ayrılma",
    };

    public static readonly Dictionary<string, string> Level = new() { ["ok"] = "Uygun", ["bilgi"] = "Bilgi", ["uyari"] = "Uyarı", ["kritik"] = "Kritik" };

    public static string Get(Dictionary<string, string> map, string? key) => key != null && map.TryGetValue(key, out var v) ? v : key ?? "";

    /// <summary>Durum anahtarı için uygun etiketi bulur</summary>
    public static string Any(string? key)
    {
        if (key == null)
        {
            return "";
        }
        if (Approval.TryGetValue(key, out var a)) return a;
        if (EnrStatus.TryGetValue(key, out var b)) return b;
        if (StudentStatus.TryGetValue(key, out var c)) return c;
        if (Level.TryGetValue(key, out var d)) return d;
        return key;
    }

    public static Dictionary<string, string> Without(Dictionary<string, string> map, params string[] keys)
    {
        return map.Where(kv => !keys.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
