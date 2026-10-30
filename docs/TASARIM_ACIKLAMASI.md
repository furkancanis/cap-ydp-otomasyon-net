# ÇAP / YDP Öğrenci Takip Otomasyonu — Tasarım Açıklaması

**Hazırlayan:** Furkan Caniş · Selçuk Üniversitesi Bilgisayar Mühendisliği 4. sınıf
**Ders:** Bilgisayar Mühendisliği Uygulamaları · **Sürüm:** 1.0 (ara sınav teslimi) · **Platform:** C# / ASP.NET Core 8

## 1. Amaç ve kapsam
Bölüme gelen ÇAP ve YDP öğrencilerinin form/kâğıt üzerinden yapılan takibini tek bir web uygulamasına taşır: alınacak dersler, hangi dersin hangi dönemde alındığı/geçildiği, belgeler, yazışmalar, yönerge kurallarına uygunluk ve mezuniyete/sertifikaya yakınlık.

Kapsam dışı (e-postadaki çerçeveye uygun olarak): başvuru değerlendirmesi ve öğrenci seçimi (dekanlık yapar, koordinatör kabul edilen öğrenciyi ekler), öğrencinin ana dal dersleri, OBİS entegrasyonu (tasarım buna hazır).

## 2. Teknoloji ve mimari
| Katman | Seçim | Gerekçe |
|---|---|---|
| Sunucu | C# · ASP.NET Core 8 MVC | Tipli, hızlı, Visual Studio ile kolay geliştirme |
| Veri erişimi | Npgsql + Dapper (parametreli SQL) | Şema üzerinde tam denetim (tetikleyiciler), SQL enjeksiyonuna karşı güvenli |
| Veritabanı | PostgreSQL 16 (Neon) | Canlı ortam, ücretsiz |
| Barındırma | Docker → Render/Railway | Ücretsiz, HTTPS hazır |
| Arayüz | Razor görünümler + sade CSS/JS | Selçuk Ün. kurumsal sayfa düzenine yakın; satır içi betik yok (CSP uyumlu) |

```
Tarayıcı ──> Middleware (güvenlik başlıkları, veritabanı oturumu)
               └─> Filtreler (Roles: rol kontrolü, CsrfFilter: CSRF)
                     └─> Controllers
                           └─> Services  ← TEK veri giriş noktası (form, CSV, ileride OBİS)
                                 ├─> Rules / Progress (kural motoru, ilerleme hesabı)
                                 ├─> Audit (değiştirilemez işlem kaydı) · Notify (bildirim)
                                 └─> Db (Npgsql + Dapper, parametreli sorgular) ──> PostgreSQL
```

**OBİS'e hazırlık:** Öğrenci kaydı `StudentService.Upsert()` üzerinden yapılır; form, CSV içe aktarma ve ileride OBİS aynı fonksiyonu kullanacaktır. Benzersiz anahtar **öğrenci numarasıdır**; `students.data_source` (manuel/csv/obis) ve `external_ref` alanları dış kaynağı izler. Katalog da CSV ile içe aktarılabilir, öğrenci listesi CSV olarak dışa aktarılabilir.

## 3. Roller
| Rol | Yetki |
|---|---|
| **Koordinatör** | Öğrenci ekler (hesap otomatik açılır), onay/ret, ek belge ister, karta özel not, kural ve ayar yönetimi, düzeltme/iptal, mezuniyet-sertifika-kayıt silme kararı |
| **Öğrenci** | Yalnızca kendi kaydı: belge yükleme, katalogdan ders seçme, dönem dersi ve not girişi, transkript, mesajlaşma, durumunu görme |
| **Bölüm Başkanı** | Yalnızca izleme: tüm kayıtlar ve işlem geçmişi; hiçbir POST işlemine yetkisi yok (sunucu tarafında 403) |

Hesapları yalnızca koordinatör açar; öğrenci kaydı yoktur. Kullanıcı adı = öğrenci no; ilk girişte geçici şifre değiştirilmek zorundadır.

## 4. Veri modeli (özet)
`students` · `users` · `terms` (akademik dönem) · `courses` (katalog) · `student_courses` (öğrencinin alacağı ders listesi/protokol) · `enrollments` (**her deneme ayrı satır**: dönem, durum, not, geçtiği dönem, onay durumu) · `grade_submissions` (dönem sonu not girişleri, transkripte bağlı) · `documents` + `document_blobs` (sürümlü belge arşivi) · `document_requests` · `messages` · `notifications` · `coordinator_notes` · `gano_records` (kaynağıyla) · `course_requests` (katalog dışı ders talebi) · `rule_sets` (sürümlü yönerge kuralları) · `rule_checks` (kontrol anlık görüntüleri) · `decisions` · `audit_log` · `settings` · `sessions` · `login_attempts`.

Ders durumları: *planlandı, alındı, geçti, kaldı, muaf, çıkarıldı*. Onay durumları: *onay bekliyor, onaylandı, reddedildi, iptal*.

## 5. Onay mantığı
- Öğrencinin girdiği ders listesi kaydı, dönem dersi, not ve belge **"onay bekliyor"** ile başlar; onaylanana kadar kural hesaplarına girmez.
- Onaylanan kayıt **kilitlenir**; öğrenci yalnızca bekleyen kendi kaydını geri çekebilir.
- Hatalı onaylı kaydı yalnızca koordinatör **gerekçe yazarak** düzeltir; eski/yeni değer işlem kaydına yazılır, kayıttaki düzeltme sayacı artar.
- Reddedilen/iptal edilen kayıt **silinmez**, o durumuyla görünür kalır.
- **Mevcut öğrenciler:** koordinatör geçmiş dönem ders ve notlarını "geriye dönük kayıt" olarak girer; bu kayıtlar onay kilidine takılmaz.

## 6. Ders takibi
- Öğrencinin ders listesi; her ders için alındığı dönem, geçtiği dönem, not, durum ve deneme sayısı.
- Tamamlanan/kalan ders ve AKTS tek bakışta (ilerleme çubuğu, özet).
- Koordinatör listeye ders ekler/çıkarır — gerekçe ve tarih kaydedilir; **geçilmiş dersler ve notlar etkilenmez** (listeden çıkarılan ama geçilmiş dersler ayrıca gösterilir).
- Katalog: ekleme, güncelleme, pasife alma (değişiklik geçmişiyle). ÇAP protokolü (EEM→BM, 135 AKTS) şablon olarak tanımlı.
- Öğrenci dersi yalnızca katalogdan seçer (kod/ad araması); katalog dışı ders için koordinatöre talep gönderir.

## 7. Belge yönetimi
- Her öğrencinin kartında tür ve durumlarıyla belge arşivi (imzalı formlar, transkript, YK kararı, muafiyet formu…).
- **Dönem sonu ÇAP/YDP transkripti yüklenmeden notlar onaya gönderilemez** (sunucu tarafında zorunlu).
- Ek belge isteği: bekliyor → yüklendi → onaylandı/reddedildi akışı izlenebilir.
- Yenilenen/reddedilen belgenin eski sürümü arşivde kalır; öğrenci belge silemez.
- Dosyalar veritabanında saklanır (bulut sunucularında disk kalıcılığı gerekmez), SHA-256 özeti kaydedilir.

## 8. Mesajlaşma ve bildirim
Her öğrenci için koordinatörle tek yazışma dizisi; mesaj silinemez/düzenlenemez, dosya eklenebilir (öğrencinin arşivine "mesaj eki" olarak girer). Yeni mesaj, onay/ret, belge isteği ve kural uyarılarında uygulama içi bildirim (zil sayacı) oluşur. Okundu bilgisi tutulur.

## 9. İşlem kaydı (denetim)
Giriş/çıkış, görüntülenen belgeler dahil her işlem: kim, ne zaman, hangi kayıt, **eski → yeni değer**, gerekçe, IP. `audit_log` tablosu veritabanı tetikleyicisiyle **UPDATE/DELETE/TRUNCATE'e kapalıdır**. Ayrıca tüm iş tablolarında DELETE tetikleyiciyle engellenmiştir — hiçbir veri tamamen silinemez, yanlış kayıt "iptal" olarak işaretlenir. Bölüm başkanı kaydı filtreleyerek görebilir.

## 10. Yönerge kuralları
Sayısal kurallar koda gömülü değildir; `rule_sets` tablosunda **sürümlü** tutulur ve koordinatör "Yönerge Kuralları" ekranından yeni sürüm oluşturur. Eski sürümler silinmez; her kontrol (`rule_checks`) kullandığı sürüm kimliği ve parametrelerle değiştirilemez şekilde saklanır, yani yönerge değişince geçmiş kontroller yeniden yazılmaz.

| Kontrol | ÇAP (varsayılan) | YDP (varsayılan) |
|---|---|---|
| Ana dal GANO | 2,72 altına 2. düşüşte kayıt silme uyarısı (md. 9/e) | 2,30 altı → kritik (md. 7/5) |
| Program ortalaması | 2,00 / bir defalık hak (Yönetmelik 17/4) | Üst üste 2 dönem 2,00 altı → kritik (md. 7/6) |
| Üst üste ders almama | 2 dönem (md. 9/d), dondurulan dönem sayılmaz | 2 dönem (md. 7/4) |
| Ders yükü | Protokol 135 AKTS + yarıyıla göre teknik seçmeli sayıları | En az 6 ders ve 30 AKTS (md. 5/g) |
| Azami süre | 14 yarıyıl (2547 s. K. 44/c) | parametrik (0 = kapalı) |
| Diğer | Eksik transkript, notu girilmemiş ders, koşullu geçer notlar | aynı |
| Tamamlama | Mezuniyet koşulları (md. 8/c) bilgisi | Sertifika koşulları (md. 7/8) bilgisi |

Yalnızca onaylı kayıtlar hesaba girer. Sonuçlar **uyarıdır**; koordinatör "Kararlar" sekmesinden mezuniyet/sertifika/kayıt silme/dondurma kararını gerekçe ve YK karar no ile verir. GANO yalnızca koordinatör tarafından, **kaynağıyla** girilir.

## 11. Güvenlik önlemleri
| Tehdit | Önlem |
|---|---|
| SQL enjeksiyonu | Tüm sorgular Npgsql parametreli komutları (Dapper); tablo/kolon adları beyaz liste regex'i |
| XSS | Razor tüm çıktıları otomatik HTML kaçışından geçirir; CSP `script-src 'self'` (satır içi betik/stil yok) |
| CSRF | Oturuma bağlı rastgele belirteç, tüm POST'larda `hash_equals` ile doğrulama (aksi 419); SameSite=Lax çerez |
| Yetkisiz erişim / IDOR | Rol kontrolü rota düzeyinde; öğrenci kimliği her zaman oturumdan alınır; başkasının belgesi 404 döner |
| Kaba kuvvet | Hesap başına 5 hatalı denemede 15 dk kilit; IP başına 15 dk'da 20 deneme sınırı; sabit süreli doğrulama |
| Oturum | Veritabanında oturum, girişte ID yenileme (session fixation), 30 dk hareketsizlik / 8 saat mutlak süre, HttpOnly + Secure (HTTPS) çerez |
| Parola | PBKDF2-SHA256, 210.000 tur, rastgele tuz, en az 8 karakter + harf + rakam, geçici şifre ilk girişte zorunlu değişim |
| Dosya yükleme | Uzantı beyaz listesi + gerçek içerik türü (finfo/magic byte) eşleşmesi, boyut sınırı, çift uzantı engeli, PDF'te JavaScript/Launch/EmbeddedFile/XFA taraması, bozuk PDF/görüntü kontrolü, görüntüde kod taraması; dosyalar web kökünde değil veritabanında; sunarken `nosniff`, görüntüler CSP sandbox ile |
| Clickjacking & başlıklar | `X-Frame-Options: DENY`, `frame-ancestors 'none'`, `Referrer-Policy`, `Permissions-Policy`, HSTS (HTTPS'te), sunucu sürüm bilgisi gizli |
| Açık yönlendirme | Yönlendirme yalnızca uygulama içi yollara |
| CSV/Excel enjeksiyonu | Dışa aktarımda `= + - @` ile başlayan hücreler etkisizleştirilir |
| Veri bütünlüğü | Silme/değiştirme tetikleyicileri; hata ayrıntıları üretimde gizli |
| Kişisel veri | Yalnızca sentetik veri; arama motoru indekslemesi kapalı (`noindex`) |

## 12. Yapılan testler (yalnızca kendi uygulamam, kendi test ortamımda)
Testler `tests/` klasöründe, uygulamaya HTTP üzerinden istek atan Python betikleridir: `tests/security_and_flow.py` (42 test), `tests/admin_flow.py` (15 test) ve `tests/smoke.py` (3 rolün eriştiği tüm sayfalar). Proje önce PHP ile geliştirildi; bu testlerin tamamı PHP sürümünde çalıştırılıp geçti. C# (.NET) sürümü aynı veritabanı şemasını ve iş kurallarını kullanır; aynı testler `ci/build-test.yml` ile GitHub Actions üzerinde gerçek PostgreSQL'e karşı çalışacak şekilde hazırlandı. Ara teslim itibarıyla .NET sürümü canlı ortamda üç rolle elle denendi; otomatik test sonuçları depo üzerinden izlenecektir.

**İş akışı:** öğrenci ekleme + geçici şifre → zorunlu şifre değişimi → belge yükleme → katalogdan ders → koordinatör onayı → kilit kontrolü → transkriptsiz not girişinin engellenmesi → not onayı → gerekçeli düzeltme ve işlem kaydı → mesaj + bildirim → GANO ile kural uyarısı.

**Sızma testleri:** CSRF'siz POST (419), SQLi ile giriş ve aramada SQLi, IDOR (başka öğrencinin belgesi), öğrencinin koordinatör sayfası/işlemleri (403), bölüm başkanının değişiklik denemesi (403), girişsiz erişim, `.pdf` uzantılı sunucu betiği (PHP kabuğu), çift uzantı, `.exe`, JavaScript gömülü PDF, yarım/bozuk PDF ve PNG, boyut aşımı, mesajda `<script>` (XSS), güvenlik başlıkları, çerez bayrakları, session fixation, kaba kuvvet kilidi, açık yönlendirme, silme uç noktası olmaması. Veritabanı düzeyinde `DELETE FROM messages` ve `UPDATE audit_log` denemeleri tetikleyicilerce reddedildi.

## 13. Bilinen sınırlamalar / sonraki adımlar
- Bildirimler uygulama içidir; e-posta (SMTP) bildirimi sonraki sürümde.
- OBİS bağlantısı yok (tasarım hazır: tek giriş noktası + CSV).
- Dosyalar veritabanında tutulur; büyük ölçekte nesne depolamaya (S3) taşınabilir.
- ÇAP programı not ortalaması alt sınırı yönetmelikte 100'lük (70) verildiğinden 4'lük karşılığı (varsayılan 2,00) koordinatörce kurallardan güncellenmelidir.
