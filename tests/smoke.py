"""Tüm sayfaları her rolle gezen duman testi: HTTP durum ve PHP uyarısı kontrolü."""
import re, sys, requests
BASE = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8080"
BAD = re.compile(r"(Warning|Notice|Deprecated|Fatal error|Uncaught|Stack trace)", re.I)

def login(user, pw):
    s = requests.Session()
    r = s.get(BASE + "/giris")
    tok = re.search(r'name="_csrf" value="([^"]+)"', r.text).group(1)
    r = s.post(BASE + "/giris", data={"_csrf": tok, "username": user, "password": pw}, allow_redirects=True)
    assert "Çıkış" in r.text, f"login failed {user}"
    return s

fails = 0
def check(s, path, expect=200):
    global fails
    r = s.get(BASE + path, allow_redirects=False)
    bad = BAD.search(r.text) if r.status_code == 200 else None
    ok = r.status_code == expect and not bad
    if not ok:
        fails += 1
        snippet = r.text[max(0, bad.start()-200):bad.start()+300] if bad else ""
        print(f"FAIL {r.status_code} {path} {snippet}")
    return r

c = login("koordinator", "Koordinator123!")
pages = ["/koordinator", "/koordinator/onay", "/koordinator/ogrenciler", "/koordinator/ogrenciler?tur=CAP&q=kaya", "/koordinator/ogrenci/yeni",
         "/koordinator/katalog", "/koordinator/katalog/1", "/koordinator/ders-talepleri", "/koordinator/donemler", "/koordinator/kurallar",
         "/koordinator/kurallar?yeni=CAP", "/koordinator/kurallar?yeni=YDP", "/koordinator/kullanicilar", "/koordinator/islem-kaydi",
         "/koordinator/islem-kaydi?sayfa=2", "/koordinator/ayarlar", "/bildirimler", "/sifre", "/belge/1"]
tabs = ["ozet", "liste", "dersler", "notlar", "belgeler", "mesajlar", "kartnotu", "gano", "kararlar", "kontroller", "gecmis"]
for p in pages: check(c, p)
for sid in range(1, 6):
    for t in tabs: check(c, f"/koordinator/ogrenci/{sid}?tab={t}")
r = c.get(BASE + "/koordinator/ogrenciler/disa-aktar"); assert r.status_code == 200 and "student_no" in r.text

h = login("baskan", "Baskan123!")
for p in ["/baskan", "/koordinator/ogrenciler", "/koordinator/katalog", "/koordinator/kurallar", "/koordinator/islem-kaydi", "/koordinator/donemler", "/koordinator/ders-talepleri"]:
    check(h, p)
for t in tabs: check(h, f"/koordinator/ogrenci/1?tab={t}")
for p in ["/koordinator", "/koordinator/onay", "/koordinator/kullanicilar", "/koordinator/ayarlar", "/ogrenci"]:
    check(h, p, 403)

for u in ["222101001", "232101002", "222101003", "212101004", "222101005"]:
    st = login(u, "Ogrenci123!")
    for p in ["/ogrenci", "/ogrenci/dersler", "/ogrenci/notlar", "/ogrenci/belgeler", "/ogrenci/mesajlar", "/bildirimler"]:
        check(st, p)
    for p in ["/koordinator", "/koordinator/ogrenci/1", "/baskan", "/koordinator/islem-kaydi"]:
        check(st, p, 403)
print("smoke fails:", fails)
sys.exit(1 if fails else 0)
