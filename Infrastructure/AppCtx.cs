using System.Security.Cryptography;
using CapYdp.Services;

namespace CapYdp.Infrastructure;

/// <summary>İşlemi yapan kullanıcı</summary>
public sealed record Actor(long Id, string Username, string Role, string FullName, string? Email, long? StudentId, bool MustChangePassword);

/// <summary>
/// Servislerin ihtiyaç duyduğu ortam: veritabanı, işlemi yapan kullanıcı, IP ve önbellek.
/// Web isteğinde RequestCtx, kurulum (seed) sırasında SeedCtx kullanılır.
/// </summary>
public interface IAppCtx
{
    Db Db { get; }
    Actor? User { get; }
    string Ip { get; }
    string UserAgent { get; }
    Dictionary<string, object?> Cache { get; }
}

/// <summary>Komut satırı / ilk kurulum bağlamı</summary>
public sealed class SeedCtx : IAppCtx, IDisposable
{
    public SeedCtx(Db db) => Db = db;
    public Db Db { get; }
    public Actor? User { get; set; }
    public string Ip => "kurulum";
    public string UserAgent => "kurulum";
    public Dictionary<string, object?> Cache { get; } = new();

    public void ActAs(long userId)
    {
        User = AuthService.LoadActor(this, userId);
        Cache.Clear();
    }

    public void Dispose() => Db.Dispose();
}

/// <summary>
/// HTTP isteği bağlamı: veritabanı bağlantısı, veritabanında saklanan oturum,
/// CSRF belirteci, flash mesajları ve oturumdaki kullanıcı.
/// </summary>
public sealed class RequestCtx : IAppCtx, IDisposable
{
    public const string CookieName = "CAPSESS";
    public const int IdleTimeout = 1800;      // 30 dk hareketsizlik
    public const int AbsoluteTimeout = 28800; // 8 saat

    private readonly IHttpContextAccessor _http;
    private Db? _db;
    private Dictionary<string, string> _session = new();
    private string? _sessionId;
    private bool _loaded;
    private Actor? _user;
    private bool _userLoaded;

    public RequestCtx(IHttpContextAccessor http)
    {
        _http = http;
    }

    private HttpContext Http => _http.HttpContext!;

    public Db Db => _db ??= new Db();

    public Dictionary<string, object?> Cache { get; } = new();

    public string Ip
    {
        get
        {
            var ip = Http.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0";
            if (ip.StartsWith("::ffff:"))
            {
                ip = ip[7..];
            }
            return ip.Length > 45 ? ip[..45] : ip;
        }
    }

    public string UserAgent
    {
        get
        {
            var ua = Http.Request.Headers.UserAgent.ToString();
            return ua.Length > 255 ? ua[..255] : ua;
        }
    }

    public bool IsHttps => Http.Request.IsHttps;

    // ------------------------------------------------------------------
    //  Oturum (veritabanında)
    // ------------------------------------------------------------------

    public void LoadSession()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        var sid = Http.Request.Cookies[CookieName];
        if (!string.IsNullOrEmpty(sid) && sid.Length <= 128)
        {
            var row = Db.One("SELECT data, last_activity FROM sessions WHERE id = @id", new { id = Json.Sha256Hex(sid) });
            if (row != null && row.L("last_activity") >= Clock.UnixNow() - AbsoluteTimeout)
            {
                _sessionId = sid;
                _session = Json.Decode<Dictionary<string, string>>(row.S("data")) ?? new();
            }
        }
        var now = Clock.UnixNow();
        if (_session.ContainsKey("uid"))
        {
            var last = long.TryParse(Get("_last"), out var l) ? l : now;
            var born = long.TryParse(Get("_born"), out var b) ? b : now;
            if (now - last > IdleTimeout || now - born > AbsoluteTimeout)
            {
                _session.Clear();
                Regenerate();
                AddFlash("info", "Oturumunuzun süresi doldu, lütfen tekrar giriş yapın.");
            }
        }
        Set("_last", now.ToString());
    }

    public void SaveSession()
    {
        if (!_loaded)
        {
            return;
        }
        // Yalnızca boş olmayan oturumlar saklanır
        var meaningful = _session.Keys.Any(k => k != "_last");
        if (!meaningful && _sessionId == null)
        {
            return;
        }
        if (_sessionId == null)
        {
            _sessionId = NewId();
        }
        var hid = Json.Sha256Hex(_sessionId);
        var data = Json.Encode(_session);
        var n = Db.Update("sessions", new Dictionary<string, object?> { ["data"] = data, ["last_activity"] = (int)Clock.UnixNow() }, "id = @id", new { id = hid });
        if (n == 0)
        {
            Db.Exec("INSERT INTO sessions (id, data, last_activity) VALUES (@id, @data, @t) ON CONFLICT (id) DO UPDATE SET data = EXCLUDED.data, last_activity = EXCLUDED.last_activity",
                new { id = hid, data, t = (int)Clock.UnixNow() });
        }
        if (Random.Shared.Next(100) == 0)
        {
            Db.Exec("DELETE FROM sessions WHERE last_activity < @t", new { t = (int)(Clock.UnixNow() - AbsoluteTimeout) });
        }
        Http.Response.Cookies.Append(CookieName, _sessionId, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = IsHttps,
            Path = "/",
            IsEssential = true,
        });
    }

    /// <summary>Oturum kimliğini yeniler (session fixation koruması)</summary>
    public void Regenerate()
    {
        if (_sessionId != null)
        {
            Db.Exec("DELETE FROM sessions WHERE id = @id", new { id = Json.Sha256Hex(_sessionId) });
        }
        _sessionId = NewId();
        // değişti
    }

    public void DestroySession()
    {
        _session.Clear();
        Regenerate();
        _user = null;
        _userLoaded = true;
    }

    private static string NewId() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(36)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public string? Get(string key) => _session.TryGetValue(key, out var v) ? v : null;

    public void Set(string key, string value)
    {
        _session[key] = value;
        // değişti
    }

    public void Remove(string key)
    {
        if (_session.Remove(key))
        {
            // değişti
        }
    }

    public void ClearAll()
    {
        _session.Clear();
        // değişti
    }

    public T? GetJson<T>(string key) => Json.Decode<T>(Get(key));

    public void SetJson(string key, object value) => Set(key, Json.Encode(value));

    public T? Take<T>(string key)
    {
        var v = GetJson<T>(key);
        Remove(key);
        return v;
    }

    // ------------------------------------------------------------------
    //  Kullanıcı
    // ------------------------------------------------------------------

    public Actor? User
    {
        get
        {
            if (_userLoaded)
            {
                return _user;
            }
            _userLoaded = true;
            if (long.TryParse(Get("uid"), out var uid))
            {
                _user = AuthService.LoadActor(this, uid);
            }
            return _user;
        }
    }

    public void SetUser(long uid)
    {
        Set("uid", uid.ToString());
        Set("_born", Clock.UnixNow().ToString());
        Set("_last", Clock.UnixNow().ToString());
        _userLoaded = false;
        _user = null;
    }

    public void ReloadUser()
    {
        _userLoaded = false;
    }

    public string Role => User?.Role ?? "";
    public bool IsCoordinator => Role == "koordinator";
    public bool IsHead => Role == "bolum_baskani";
    public bool IsStudent => Role == "ogrenci";

    // ------------------------------------------------------------------
    //  CSRF ve flash
    // ------------------------------------------------------------------

    public string CsrfToken()
    {
        var t = Get("_csrf");
        if (string.IsNullOrEmpty(t))
        {
            t = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            Set("_csrf", t);
        }
        return t;
    }

    public bool CsrfValid(string? sent)
    {
        var t = Get("_csrf");
        if (string.IsNullOrEmpty(t) || string.IsNullOrEmpty(sent))
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(t), System.Text.Encoding.UTF8.GetBytes(sent));
    }

    public void AddFlash(string type, string msg)
    {
        var list = GetJson<List<string[]>>("_flash") ?? new List<string[]>();
        list.Add(new[] { type, msg });
        SetJson("_flash", list);
    }

    public List<string[]> TakeFlashes() => Take<List<string[]>>("_flash") ?? new List<string[]>();

    public string Old(string key, string def = "")
    {
        var d = GetJson<Dictionary<string, string>>("_old");
        return d != null && d.TryGetValue(key, out var v) ? v : def;
    }

    public void Dispose()
    {
        _db?.Dispose();
    }
}
