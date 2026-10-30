# ÇAP / YDP Öğrenci Takip Otomasyonu (C# / ASP.NET Core 8)

Selçuk Üniversitesi Teknoloji Fakültesi Bilgisayar Mühendisliği Bölümü için **Çift Anadal (ÇAP)** ve **Yandal (YDP)** öğrenci takip sistemi.
*Bilgisayar Mühendisliği Uygulamaları dersi projesi — Furkan Caniş*

- **Teknoloji:** C# · ASP.NET Core 8 MVC (Razor görünümler) · PostgreSQL (Npgsql + Dapper)
- **Arayüz:** Selçuk Üniversitesi kurumsal sayfa düzenine yakın (lacivert #1c458b, sol menü, ekmek kırıntısı)
- **Veri:** Yalnızca **sentetik** test verisi — veritabanı boşsa ilk açılışta tablolar ve örnek veri otomatik kurulur

## Çalıştırma

1. **.NET 8 SDK** kurun: https://dotnet.microsoft.com/download/dotnet/8.0 (Visual Studio 2022 kuruluysa zaten vardır).
2. Bir PostgreSQL veritabanı gösterin (en kolayı ücretsiz **Neon**, ayrıntı: [`docs/KURULUM_VE_CANLIYA_ALMA.md`](docs/KURULUM_VE_CANLIYA_ALMA.md)):
   proje klasöründe `appsettings.Local.json` oluşturup (Git'e gönderilmez) `ConnectionStrings:Default` alanına Neon adresini yazın
   (`postgresql://...` biçimi de kabul edilir) **veya** `DATABASE_URL` ortam değişkeni tanımlayın.
3. Çalıştırın:
   - Visual Studio: `CapYdp.csproj` dosyasını açın → **F5**
   - Komut satırı: `dotnet run` → http://localhost:5080
   - Docker (PostgreSQL dahil): `docker compose up --build` → http://localhost:8080

## Test hesapları

| Rol | Kullanıcı adı | Şifre |
|---|---|---|
| Koordinatör | `koordinator` | `Koordinator123!` |
| Bölüm Başkanı (salt okunur) | `baskan` | `Baskan123!` |
| Öğrenci — ÇAP, ilerlemiş | `222101001` | `Ogrenci123!` |
| Öğrenci — ÇAP, yeni kabul | `232101002` | `Ogrenci123!` |
| Öğrenci — YDP | `222101003` | `Ogrenci123!` |
| Öğrenci — YDP, kritik uyarılı | `212101004` | `Ogrenci123!` |
| Öğrenci — ÇAP, GANO uyarılı | `222101005` | `Ogrenci123!` |

## Proje yapısı

```
Program.cs                 Başlangıç: veritabanı, güvenlik başlıkları, oturum, MVC
Controllers/               Auth, Coordinator (öğrenci kartı, onaylar), Catalog, Admin, Student
Services/                  İş kuralları — TÜM veri girişi buradan geçer (form, CSV, ileride OBİS)
  Rules.cs                 Sürümlü yönerge kural motoru
  Progress.cs              Ders ilerleme / ortalama hesabı
  FileGuard.cs             Dosya yükleme güvenlik denetimi
Infrastructure/            Db (Npgsql+Dapper), Row, oturum/CSRF (AppCtx), şifreleme, görünüm yardımcıları (H)
Web/                       Rol ve CSRF filtreleri, ortak denetleyici tabanı
Views/                     Razor arayüzleri
Data/schema.sql            PostgreSQL şeması + silme/değiştirme engelleyici tetikleyiciler
Data/Seeder.cs             Sentetik örnek veri
wwwroot/assets/            CSS, JS
tests/                     smoke.py, security_and_flow.py (42 test), admin_flow.py (15 test)
ci/build-test.yml          GitHub Actions iş akışı (.github/workflows/ altına taşınınca her push'ta derleme + testler)
docs/                      Tasarım açıklaması, kurulum/canlıya alma, teslim e-postası taslağı
```

## Testler
```bash
dotnet run --urls http://127.0.0.1:8080      # ayrı terminalde
python3 tests/smoke.py
python3 tests/security_and_flow.py
python3 tests/admin_flow.py
```
