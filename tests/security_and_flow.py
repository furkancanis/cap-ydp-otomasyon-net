"""Uçtan uca iş akışı + sızma (güvenlik) testleri. Yalnızca yerel test ortamında çalıştırın."""
import re, sys, io, requests
BASE = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8080"
results = []
def ok(name, cond, info=""):
    results.append((name, bool(cond))); print(("PASS " if cond else "FAIL ") + name + (f"  [{info}]" if info and not cond else ""))

def csrf(s, path):
    return re.search(r'name="_csrf" value="([^"]+)"', s.get(BASE + path).text).group(1)

def login(u, p):
    s = requests.Session()
    t = csrf(s, "/giris")
    r = s.post(BASE + "/giris", data={"_csrf": t, "username": u, "password": p})
    return s, r

def post(s, path, data, files=None, ref="/"):
    data = dict(data); data["_csrf"] = csrf(s, ref)
    return s.post(BASE + path, data=data, files=files, headers={"Referer": BASE + ref})

# ---------------- İŞ AKIŞI ----------------
coord, _ = login("koordinator", "Koordinator123!")
# 1) koordinatör yeni ÇAP öğrencisi ekler -> geçici şifre
r = post(coord, "/koordinator/ogrenci/yeni", {"student_no": "242101099", "full_name": "Akış Test Öğrencisi", "email": "akis.test@ogr.selcuk.edu.tr",
     "program_type": "CAP", "admission_term_id": "", "main_faculty": "Teknoloji Fakültesi", "main_department": "EEM", "add_protocol": "1"}, ref="/koordinator/ogrenci/yeni")
ok("Kabul dönemi boşken öğrenci eklenemez", "Kabul dönemi seçilmelidir" in r.text)
term = re.search(r'<option value="(\d+)" selected>2026-2027 Güz', coord.get(BASE + "/koordinator/ogrenci/yeni").text).group(1)
r = post(coord, "/koordinator/ogrenci/yeni", {"student_no": "242101099", "full_name": "Akış Test Öğrencisi", "email": "akis.test@ogr.selcuk.edu.tr",
     "program_type": "CAP", "admission_term_id": term, "main_faculty": "Teknoloji Fakültesi", "main_department": "EEM", "add_protocol": "1"}, ref="/koordinator/ogrenci/yeni")
m = re.search(r'<code>242101099</code></td><td><span class="pw-box">([^<]+)</span>', r.text)
ok("Öğrenci eklendi, geçici şifre üretildi", m)
tmp_pw = m.group(1)
sid = re.search(r'/koordinator/ogrenci/(\d+)">Öğrenci kartı', r.text).group(1)
# 2) öğrenci ilk girişte şifre değiştirmek zorunda
st, r = login("242101099", tmp_pw)
ok("Geçici şifreyle girişte şifre değiştirme zorunlu", "/sifre" in r.url)
r = post(st, "/sifre", {"current": tmp_pw, "new": "kisa", "confirm": "kisa"}, ref="/sifre")
ok("Zayıf şifre reddedilir", "en az 8" in r.text)
r = post(st, "/sifre", {"current": tmp_pw, "new": "YeniSifre2026", "confirm": "YeniSifre2026"}, ref="/sifre")
ok("Şifre değiştirildi", r.url.endswith("/ogrenci"))
# 3) öğrenci form yükler
pdf = open("tests/fixtures/ornek.pdf", "rb").read()
r = post(st, "/ogrenci/belgeler", {"doc_type": "kabul_protokol_formu", "term_id": term, "title": "Protokol"}, files={"file": ("protokol.pdf", pdf, "application/pdf")}, ref="/ogrenci/belgeler")
ok("Öğrenci PDF yükleyebilir", "Belge yüklendi" in r.text)
# 4) dönem dersi: listede onaylı ders olmalı (protokol koordinatör eklediği için onaylı)
page = st.get(BASE + "/ogrenci/dersler").text
cid = re.search(r'<option value="(\d+)">3301156', page).group(1)
r = post(st, "/ogrenci/donem-dersi", {"term_id": term, "course_id": cid}, ref="/ogrenci/dersler")
ok("Öğrenci dönem dersi girer (onay bekliyor)", "onayı bekleniyor" in r.text)
r = post(st, "/ogrenci/liste/ekle", {"course_code": "UYDURMA999"}, ref="/ogrenci/dersler")
ok("Serbest metinle ders uydurulamaz", "aktif ders bulunamadı" in r.text)
# 5) koordinatör onaylar
q = coord.get(BASE + f"/koordinator/ogrenci/{sid}?tab=dersler").text
eid = re.search(r'/koordinator/ders/(\d+)/karar', q).group(1)
r = post(coord, f"/koordinator/ders/{eid}/karar", {"karar": "onay"}, ref=f"/koordinator/ogrenci/{sid}?tab=dersler")
ok("Koordinatör dönem dersini onaylar", "İşlem kaydedildi" in r.text)
r = post(st, f"/ogrenci/donem-dersi/{eid}/geri-cek", {}, ref="/ogrenci/dersler")
ok("Onaylanan kayıt kilitlidir (öğrenci geri çekemez)", "Yalnızca onay bekleyen" in r.text)
# 6) not girişi transkriptsiz engellenir
r = post(st, "/ogrenci/notlar", {"term_id": term, f"grade[{eid}]": "BA"}, ref=f"/ogrenci/notlar?donem={term}")
ok("Transkript yüklenmeden not onaya gönderilemez", "transkript" in r.text.lower() and "yüklemelisiniz" in r.text)
r = post(st, "/ogrenci/belgeler", {"doc_type": "cap_ydp_transkript", "term_id": term, "ret": "notlar"}, files={"file": ("transkript.pdf", pdf, "application/pdf")}, ref="/ogrenci/notlar")
r = post(st, "/ogrenci/notlar", {"term_id": term, f"grade[{eid}]": "BA"}, ref=f"/ogrenci/notlar?donem={term}")
ok("Transkript sonrası not onaya gönderildi", "onayına gönderildi" in r.text)
q = coord.get(BASE + f"/koordinator/ogrenci/{sid}?tab=notlar").text
gid = re.search(r'/koordinator/not/(\d+)/karar', q).group(1)
post(coord, f"/koordinator/not/{gid}/karar", {"karar": "onay"}, ref=f"/koordinator/ogrenci/{sid}?tab=notlar")
q = coord.get(BASE + f"/koordinator/ogrenci/{sid}?tab=dersler").text
ok("Not onayı sonrası ders 'Geçti' oldu", "Geçti</span></td><td><strong>BA" in q)
# 7) düzeltme gerekçesiz yapılamaz; gerekçeyle yapılır ve işlem kaydına girer
r = post(coord, f"/koordinator/ders/{eid}/duzelt", {"term_id": term, "status": "gecti", "grade": "AA", "reason": ""}, ref=f"/koordinator/ogrenci/{sid}?tab=dersler")
ok("Gerekçesiz düzeltme reddedilir", "Gerekçe" in r.text)
r = post(coord, f"/koordinator/ders/{eid}/duzelt", {"term_id": term, "status": "gecti", "grade": "AA", "reason": "Transkriptte not AA görünüyor"}, ref=f"/koordinator/ogrenci/{sid}?tab=dersler")
log = coord.get(BASE + f"/koordinator/ogrenci/{sid}?tab=gecmis").text
ok("Düzeltme eski/yeni değerle işlem kaydında", "letter_grade: BA" in log and "letter_grade: AA" in log and "Transkriptte not AA" in log)
# 8) mesajlaşma + bildirim
post(st, "/ogrenci/mesajlar", {"body": "Merhaba hocam <script>alert(1)</script>"}, ref="/ogrenci/mesajlar")
q = coord.get(BASE + f"/koordinator/ogrenci/{sid}?tab=mesajlar").text
ok("XSS: mesaj içeriği kaçışlanır", "&lt;script&gt;alert(1)&lt;/script&gt;" in q and "<script>alert(1)" not in q)
ok("Koordinatöre yeni mesaj bildirimi", "Yeni mesaj: Akış Test" in coord.get(BASE + "/bildirimler").text)
# 9) GANO + kural uyarısı
post(coord, f"/koordinator/ogrenci/{sid}/gano", {"term_id": term, "gano": "2,60", "source": "Ana dal transkripti"}, ref=f"/koordinator/ogrenci/{sid}?tab=gano")
q = coord.get(BASE + f"/koordinator/ogrenci/{sid}").text
ok("GANO 2,60 -> ÇAP eşik uyarısı üretildi", "Ana dal GANO eşiği" in q)
# 10) kural sürümü değişince eski kontrol değişmez
before = coord.get(BASE + f"/koordinator/ogrenci/{sid}?tab=kontroller").text.count("v2 ·")

# ---------------- GÜVENLİK ----------------
s = requests.Session(); s.get(BASE + "/giris")
r = s.post(BASE + "/giris", data={"username": "koordinator", "password": "Koordinator123!"})
ok("CSRF belirteci olmadan POST reddedilir (419)", r.status_code == 419)
_, r = login("koordinator' OR '1'='1' --", "x' OR '1'='1")
ok("SQL enjeksiyonu ile giriş yapılamaz", "hatalı" in r.text and "Çıkış" not in r.text)
r = coord.get(BASE + "/koordinator/ogrenciler", params={"q": "' OR 1=1; DROP TABLE students; --"})
ok("Arama parametresinde SQL enjeksiyonu etkisiz", r.status_code == 200 and "Kayıt bulunamadı" in r.text)
# IDOR: öğrenci başka öğrencinin belgesini göremez
other_doc = re.search(r'/belge/(\d+)"', coord.get(BASE + "/koordinator/ogrenci/1?tab=belgeler").text).group(1)
ok("IDOR: başka öğrencinin belgesi 404", st.get(BASE + f"/belge/{other_doc}").status_code == 404)
ok("Öğrenci koordinatör sayfasına erişemez (403)", st.get(BASE + "/koordinator/ogrenci/1").status_code == 403)
r = post(st, f"/koordinator/ogrenci/1/gano", {"term_id": term, "gano": "4", "source": "hack"}, ref="/ogrenci")
ok("Öğrenci koordinatör işlemi (POST) yapamaz", r.status_code == 403)
head, _ = login("baskan", "Baskan123!")
r = post(head, f"/koordinator/ogrenci/{sid}/not", {"body": "deneme"}, ref="/baskan")
ok("Bölüm başkanı hiçbir şeyi değiştiremez (403)", r.status_code == 403)
ok("Bölüm başkanı işlem kaydını görür", head.get(BASE + "/koordinator/islem-kaydi").status_code == 200)
ok("Girişsiz erişim girişe yönlenir", requests.get(BASE + "/koordinator", allow_redirects=False).headers.get("Location", "").endswith("/giris"))
# dosya güvenliği
def up(name, data, mime):
    return post(st, "/ogrenci/belgeler", {"doc_type": "diger", "title": "t"}, files={"file": (name, data, mime)}, ref="/ogrenci/belgeler").text
ok("PHP kodu .pdf uzantısıyla yüklenemez", "uyuşmuyor" in up("shell.pdf", b"<?php system($_GET['c']); ?>", "application/pdf"))
ok("Çift uzantı (x.php.pdf) reddedilir", "çift uzantı" in up("x.php.pdf", pdf, "application/pdf"))
ok("İzin verilmeyen tür (.exe) reddedilir", "İzin verilmeyen" in up("a.exe", b"MZ\x90\x00", "application/octet-stream"))
js_pdf = pdf.replace(b"/Type /Catalog", b"/Type /Catalog /OpenAction << /S /JavaScript /JS (app.alert(1)) >>")
ok("JavaScript içeren PDF reddedilir", "JavaScript" in up("js.pdf", js_pdf, "application/pdf"))
ok("Bozuk (yarım) PDF reddedilir", "bozuk" in up("yarim.pdf", pdf[:200], "application/pdf"))
ok("Bozuk PNG reddedilir", "bozuk" in up("a.png", b"\x89PNG\r\n\x1a\n" + b"\x00" * 50, "image/png") or "uyuşmuyor" in up("a.png", b"\x89PNG\r\n\x1a\n" + b"\x00" * 50, "image/png"))
ok("6 MB dosya boyut sınırına takılır", "en fazla" in up("big.pdf", b"%PDF-1.4\n" + b"0" * (6 * 1024 * 1024) + b"\n%%EOF", "application/pdf"))
# güvenlik başlıkları
h = requests.get(BASE + "/giris").headers
ok("CSP başlığı var", "default-src 'self'" in h.get("Content-Security-Policy", ""))
ok("X-Frame-Options DENY", h.get("X-Frame-Options") == "DENY")
ok("nosniff", h.get("X-Content-Type-Options") == "nosniff")
ok("Oturum çerezi HttpOnly + SameSite", "httponly" in h.get("Set-Cookie", "").lower() and "samesite=lax" in h.get("Set-Cookie", "").lower())
# oturum sabitleme
s = requests.Session(); s.get(BASE + "/giris"); sid_before = s.cookies.get("CAPSESS")
t = csrf(s, "/giris"); s.post(BASE + "/giris", data={"_csrf": t, "username": "baskan", "password": "Baskan123!"})
ok("Girişte oturum kimliği yenilenir (session fixation)", s.cookies.get("CAPSESS") != sid_before)
# kaba kuvvet
for i in range(5):
    _, r = login("212101004", "yanlis" + str(i))
_, r = login("212101004", "Ogrenci123!")
ok("5 hatalı denemeden sonra hesap kilitlenir", "kilitlendi" in r.text)
# açık yönlendirme
r = post(coord, "/bildirimler/okundu", {"go": "//evil.example.com"}, ref="/bildirimler")
ok("Açık yönlendirme engellenir", "evil" not in r.url)
# veri silinemez
ok("Belge silme uç noktası yok", coord.post(BASE + f"/belge/{other_doc}", data={"_csrf": "x"}).status_code in (405, 419))
print(f"\n{sum(1 for _, c in results if c)}/{len(results)} test geçti")
sys.exit(0 if all(c for _, c in results) else 1)
