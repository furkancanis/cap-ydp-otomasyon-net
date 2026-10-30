# Kurulum ve Canlıya Alma (.NET sürümü)

## 1. Veritabanı: Neon (ücretsiz PostgreSQL)
1. https://neon.tech → GitHub ile giriş → **Create project** (Region: *AWS Europe Central (Frankfurt)*, Postgres 16).
2. Dashboard → **Connect** → bağlantı adresini kopyala:
   `postgresql://neondb_owner:XXXX@ep-xxxx.eu-central-1.aws.neon.tech/neondb?sslmode=require`
3. Tabloları elle oluşturmana gerek yok; uygulama ilk açılışta şemayı ve sentetik örnek veriyi kurar.

## 2. Kendi bilgisayarında çalıştırma
1. **.NET 8 SDK** kur: https://dotnet.microsoft.com/download/dotnet/8.0 (Visual Studio 2022 ile birlikte gelir).
2. Proje klasöründe `appsettings.Local.json` dosyası oluşturup (`.gitignore` sayesinde GitHub'a gönderilmez) `ConnectionStrings:Default` değerine Neon adresini yaz:
   ```json
   "ConnectionStrings": { "Default": "postgresql://neondb_owner:XXXX@ep-xxxx.eu-central-1.aws.neon.tech/neondb?sslmode=require" }
   ```
   (Bilgisayarında PostgreSQL kuruluysa varsayılan `Host=localhost;...` satırını da kullanabilirsin.)
3. Visual Studio'da `CapYdp.csproj` → **F5**, ya da klasörde `dotnet run` → http://localhost:5080

> Bağlantı adresi şifre içerdiği için `appsettings.json` içine yazılmaz; canlıda Render'daki `DATABASE_URL` ortam değişkeni kullanılır.
> Not: Bazı okul/ev ağları 5432 portunu engeller; yerelde "bağlantı reddedildi" hatası alırsan başka bir ağdan (ör. telefon hotspot) dene.

## 3. Canlıya alma: Render (ücretsiz, Docker)
1. Kodu GitHub'a yükle (GitHub Desktop → *Add existing repository* → *Publish*, veya `git init / add / commit / push`).
2. https://render.com → GitHub ile giriş → **New + → Web Service** → depoyu seç.
3. *Language*: **Docker**, *Region*: Frankfurt, *Instance type*: **Free**.
4. **Environment Variables**:
   | Anahtar | Değer |
   |---|---|
   | `DATABASE_URL` | Neon adresi |
   | `TRUST_PROXY` | `1` |
   | `SEED_DEMO` | `1` |
   | `SHOW_TEST_ACCOUNTS` | `1` |
5. *Health Check Path*: `/saglik` → **Create Web Service**. İlk derleme 5-8 dakika sürer.
6. Kontrol: `https://ADRESIN.onrender.com/saglik` → `{"durum":"ok",...}`

> Render ücretsiz planda 15 dk işlem olmazsa uyur; ilk açılış ~30-60 sn sürebilir. Hocaya göndermeden önce adresi bir kez aç.

**Railway alternatifi:** New Project → Deploy from GitHub repo (Dockerfile otomatik) → aynı ortam değişkenleri → Settings > Networking > Generate Domain.

## 4. Veriyi sıfırlama
Neon → SQL Editor: `DROP SCHEMA public CASCADE; CREATE SCHEMA public;` → uygulamayı yeniden başlat.
(Uygulama içinden veri silinemez — bilinçli tasarım kararı.)
