"""Koordinatör yönetim ekranlarının işlevsel testi."""
import re, sys, requests
BASE = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8080"
res = []
def ok(n, c): res.append(c); print(("PASS " if c else "FAIL ") + n)
s = requests.Session()
def tok(p): return re.search(r'name="_csrf" value="([^"]+)"', s.get(BASE + p).text).group(1)
def post(p, d, ref="/koordinator", files=None):
    d = dict(d); d["_csrf"] = tok(ref); return s.post(BASE + p, data=d, files=files, headers={"Referer": BASE + ref})
post("/giris", {"username": "koordinator", "password": "Koordinator123!"}, "/giris")
# kural sürümü
page = s.get(BASE + "/koordinator/kurallar?yeni=YDP").text
params = {f"p[{k}]": v for k, v in re.findall(r'name="p\[(\w+)\]" value="([^"]*)"', page)}
for k, v in re.findall(r'name="p\[(\w+)\]" required>([^<]*)</textarea>', page): params[f"p[{k}]"] = v.replace("&quot;", '"')
params["p[anadal_gano_min]"] = "2,40"
r = post("/koordinator/kurallar", {"program_type": "YDP", "title": "Test sürüm", "valid_from": "2026-10-28", "activate": "1", **params}, "/koordinator/kurallar")
ok("Yeni kural sürümü oluşturuldu", "Yeni kural sürümü kaydedildi" in r.text and "Test sürüm" in r.text)
ok("Eski sürüm arşivde duruyor", "Senato 26/10/2023, 2023-131" in r.text)
r = post("/koordinator/kurallar", {"program_type": "YDP", "title": "Bozuk", "valid_from": "2026-10-28", **{**params, "p[min_akts]": "abc"}}, "/koordinator/kurallar")
ok("Geçersiz kural değeri reddedilir", "sayısal olmalıdır" in r.text)
# katalog
r = post("/koordinator/katalog", {"code": "TST101", "name": "Test Dersi", "ects": "3", "credit": "2", "category": "diger", "program_scope": "HER_IKISI"}, "/koordinator/katalog")
ok("Kataloğa ders eklendi", "TST101" in r.text)
cid = re.search(r'/koordinator/katalog/(\d+)">Düzenle</a></td>\s*</tr>\s*$', r.text, re.M) or re.findall(r'href="/koordinator/katalog/(\d+)"', r.text)
r = post("/koordinator/katalog", {"code": "TST101", "name": "Tekrar", "ects": "3"}, "/koordinator/katalog")
ok("Aynı kod ikinci kez eklenemez", "zaten var" in r.text)
# ders talebi kabul
req = re.search(r'/koordinator/ders-talepleri/(\d+)"', s.get(BASE + "/koordinator/ders-talepleri").text).group(1)
r = post(f"/koordinator/ders-talepleri/{req}", {"karar": "kabul", "code": "3302458", "name": "Veri Yapıları (EEM)", "ects": "4", "semester": "4", "category": "teknik_secmeli", "program_scope": "CAP"}, "/koordinator/ders-talepleri")
ok("Ders talebi kabul edilip kataloğa eklendi", "Talep sonuçlandırıldı" in r.text and "3302458" in s.get(BASE + "/koordinator/katalog?q=3302458").text)
# dönem
r = post("/koordinator/donemler", {"academic_year": "2028-2029", "season": "Güz"}, "/koordinator/donemler")
ok("Dönem eklendi", "2028-2029 Güz" in r.text)
# karar
r = post("/koordinator/ogrenci/4/karar", {"decision_type": "kayit_silme", "decision_date": "2026-10-28", "term_id": "", "reference_no": "YK 2026/99", "reason": "İki dönem üst üste ders almadı (yönerge 7/4)"}, "/koordinator/ogrenci/4?tab=kararlar")
ok("Kayıt silme kararı verildi, durum değişti", "Karar kaydedildi" in r.text and "Kaydı Silindi" in r.text)
r = post("/koordinator/ogrenci/3/karar", {"decision_type": "mezuniyet", "decision_date": "2026-10-28", "reason": "deneme deneme"}, "/koordinator/ogrenci/3?tab=kararlar")
ok("YDP öğrencisine mezuniyet kararı verilemez", "yalnızca ÇAP" in r.text)
# CSV içe aktarma
csv = "student_no;full_name;email;program_type;admission_term;main_faculty;main_department\n242101200;Csv Bir;csv.bir@ogr.selcuk.edu.tr;YDP;2026-2027 Güz;Teknoloji;Mekatronik\nabc;Hatalı;x;CAP;2026-2027 Güz;;\n"
r = post("/koordinator/ogrenci/ice-aktar", {}, "/koordinator/ogrenci/yeni", files={"csv": ("o.csv", csv.encode(), "text/csv")})
ok("CSV: 1 eklendi 1 hata", "1 öğrenci eklendi" in r.text and "Satır 3" in r.text)
# kullanıcı + ayar
r = post("/koordinator/kullanicilar", {"username": "baskan2", "full_name": "İkinci Başkan", "email": "", "role": "bolum_baskani"}, "/koordinator/kullanicilar")
ok("Personel hesabı oluşturuldu", "Geçici şifre" in r.text)
r = post("/koordinator/ayarlar", {"max_upload_mb": "8", "allowed_extensions": "pdf,png", "student_email_domain": "ogr.selcuk.edu.tr", "department_name": "Bilgisayar Mühendisliği Bölümü", "coordinator_title": "ÇAP / YDP Koordinatörlüğü"}, "/koordinator/ayarlar")
ok("Ayarlar kaydedildi", "Ayarlar kaydedildi" in r.text)
r = post("/koordinator/ayarlar", {"allowed_extensions": "pdf,php"}, "/koordinator/ayarlar")
ok("Tehlikeli uzantı ayarı reddedilir", "geçersiz" in r.text)
post("/koordinator/ayarlar", {"max_upload_mb": "5", "allowed_extensions": "pdf,jpg,jpeg,png"}, "/koordinator/ayarlar")
r = post("/koordinator/onay/toplu", {}, "/koordinator/onay")
ok("Boş toplu onay uyarı verir", "seçilmedi" in r.text)
r = post("/koordinator/kurallar/toplu-kontrol", {}, "/koordinator")
ok("Toplu kural kontrolü", "kural kontrolü yapıldı" in r.text)
print(f"{sum(res)}/{len(res)}"); sys.exit(0 if all(res) else 1)
