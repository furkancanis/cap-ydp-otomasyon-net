# Ara Sınav Teslim E-postası (taslak)

**Kime:** ilkerozkan@selcuk.edu.tr
**Konu:** ÇAP Takip Otomasyonu Arasınav - Furkan Caniş

---

Sayın Hocam,

ÇAP/YDP Öğrenci Takip Otomasyonu ara sınav teslimine ait bilgiler aşağıdadır.

**1. Canlı uygulama bağlantısı:** https://_____________.onrender.com
(Ücretsiz sunucu 15 dakika işlem olmazsa uyku moduna geçtiği için ilk açılış 30-50 saniye sürebilir.)

**2. Test hesapları** (tüm veriler sentetiktir):
| Rol | Kullanıcı adı | Şifre |
|---|---|---|
| Koordinatör | koordinator | Koordinator123! |
| Bölüm Başkanı | baskan | Baskan123! |
| Öğrenci (ÇAP) | 222101001 | Ogrenci123! |
| Öğrenci (YDP) | 222101003 | Ogrenci123! |
| Öğrenci (YDP, kural uyarılı) | 212101004 | Ogrenci123! |

**3. Kaynak kod:** https://github.com/_____________/cap-ydp-otomasyon

**4. Geliştirme durumu**

Teknoloji: C# / ASP.NET Core 8 MVC, PostgreSQL (Neon), Docker ile Render üzerinde yayın.

Tamamlanan ve çalışan modüller:
- Üç rol (koordinatör, öğrenci, salt okunur bölüm başkanı); hesapları koordinatör açar, geçici şifre ilk girişte değiştirilir.
- Öğrenci ekleme (form ve CSV içe aktarma, öğrenci no benzersiz anahtar, OBİS'e hazır tek giriş noktası), CSV dışa aktarma.
- Ders kataloğu yönetimi (ekleme, güncelleme, pasife alma, CSV), ÇAP protokol şablonu, katalog dışı ders talebi.
- Ders takibi: öğrencinin ders listesi, dönemlik kayıtlar (her deneme ayrı), alındığı/geçildiği dönem, not ve durum, tamamlanan/kalan ders ve AKTS; gerekçeli listeye ekleme/çıkarma.
- Onay mantığı: onay bekleyen kayıtlar, onaylananın kilitlenmesi, gerekçeli düzeltme ve iptal (silme yok), toplu onay ekranı; mevcut öğrenciler için geriye dönük kayıt.
- Dönem sonu not girişi (transkript yüklenmeden gönderilemez) ve koordinatör onayı.
- Belge arşivi (sürümlü, silinemez), ek belge istekleri, dosya güvenlik kontrolleri.
- Sistem içi mesajlaşma (silinemez, dosya eki) ve uygulama içi bildirimler.
- Değiştirilemez işlem kaydı (eski/yeni değerli); veritabanı tetikleyicileriyle silme engeli.
- Sürümlü yönerge kuralları (ÇAP ve YDP) ve kural motoru: GANO eşikleri, program ortalaması, üst üste ders almama, AKTS/ders sayısı, azami süre, eksik transkript; uyarılar öğrenciye ve koordinatöre bildirilir, kontroller sürümüyle saklanır.
- Kararlar: mezuniyet, sertifika, kayıt silme, dondurma, ayrılma.
- Güvenlik: parametreli sorgular, CSRF, CSP ve güvenlik başlıkları, kaba kuvvet kilidi, oturum güvenliği, dosya içerik doğrulama. Yapılan sızma testleri ve alınan önlemler depodaki docs/TASARIM_ACIKLAMASI.md dosyasında açıklanmıştır.

Henüz tamamlanmayan / sonraki aşama:
- E-posta ile bildirim (şu an yalnızca uygulama içi bildirim var).
- OBİS entegrasyonu (tasarım hazır, bağlantı yapılmadı).
- Raporlama/çıktı alma (ör. öğrenci durum raporu PDF).
- Otomatik testlerin .NET sürümünde GitHub Actions üzerinde sürekli çalıştırılması (testler PHP prototipinde çalıştırıldı; .NET için iş akışı hazır).

Saygılarımla,
Furkan Caniş
Bilgisayar Mühendisliği 4. Sınıf
