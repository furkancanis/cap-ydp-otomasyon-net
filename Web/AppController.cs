using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace CapYdp.Web;

/// <summary>Tüm denetleyicilerin ortak yardımcıları</summary>
public abstract class AppController : Controller
{
    private static readonly Regex Ctrl = new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled);

    protected RequestCtx C => HttpContext.RequestServices.GetRequiredService<RequestCtx>();

    /// <summary>Form veya sorgu dizesinden metin (kırpılmış, kontrol karakterleri temizlenmiş)</summary>
    protected string In(string key, int max = 1000)
    {
        string v = "";
        if (Request.HasFormContentType && Request.Form.ContainsKey(key))
        {
            v = Request.Form[key].ToString();
        }
        else if (Request.Query.ContainsKey(key))
        {
            v = Request.Query[key].ToString();
        }
        v = Ctrl.Replace(v, "").Trim();
        return v.Length > max ? v[..max] : v;
    }

    protected long? InLong(string key)
    {
        var v = In(key, 20);
        return long.TryParse(v, out var l) ? l : null;
    }

    protected decimal? InDec(string key) => CourseService.ParseDec(In(key, 20));

    protected bool Has(string key) => (Request.HasFormContentType && Request.Form.ContainsKey(key)) || Request.Query.ContainsKey(key);

    protected List<string> InList(string key)
    {
        if (!Request.HasFormContentType)
        {
            return new List<string>();
        }
        return Request.Form[key].Select(x => x ?? "").ToList();
    }

    /// <summary>Formdaki tüm alanlar (CSRF hariç)</summary>
    protected Dictionary<string, string> FormDict()
    {
        var d = new Dictionary<string, string>();
        if (Request.HasFormContentType)
        {
            foreach (var kv in Request.Form)
            {
                if (kv.Key != "_csrf")
                {
                    d[kv.Key] = Ctrl.Replace(kv.Value.ToString(), "").Trim();
                }
            }
        }
        return d;
    }

    /// <summary>p[anahtar] biçimindeki alanları sözlüğe çevirir</summary>
    protected Dictionary<string, string> FormPrefix(string prefix)
    {
        var d = new Dictionary<string, string>();
        foreach (var kv in FormDict())
        {
            if (kv.Key.StartsWith(prefix + "[") && kv.Key.EndsWith("]"))
            {
                d[kv.Key[(prefix.Length + 1)..^1]] = kv.Value;
            }
        }
        return d;
    }

    protected IFormFile? FileIn(string key)
    {
        if (!Request.HasFormContentType)
        {
            return null;
        }
        var f = Request.Form.Files[key];
        return f == null || f.Length == 0 && string.IsNullOrEmpty(f.FileName) ? null : f;
    }

    protected IActionResult Go(string path) => Redirect(path);

    /// <summary>Geldiği sayfaya döner (yalnızca aynı site)</summary>
    protected IActionResult Back(string fallback = "/")
    {
        var r = Request.Headers.Referer.ToString();
        if (Uri.TryCreate(r, UriKind.Absolute, out var u) && u.Host == Request.Host.Host && (Request.Host.Port == null || u.Port == Request.Host.Port))
        {
            return Redirect(u.PathAndQuery);
        }
        return Redirect(fallback);
    }

    /// <summary>
    /// POST işlemlerini sarar: UserError yakalanır ve kullanıcıya gösterilir.
    /// fn bir yol ("/...") döndürürse oraya yönlendirilir.
    /// </summary>
    protected IActionResult ActTo(Func<string?> fn, string? ok = null, string? to = null)
    {
        try
        {
            var res = fn();
            if (ok != null)
            {
                C.AddFlash("success", ok);
            }
            if (res != null && res.StartsWith('/'))
            {
                return Redirect(res);
            }
        }
        catch (UserError e)
        {
            C.AddFlash("error", e.Message);
        }
        return to != null ? Redirect(to) : Back();
    }

    protected IActionResult Act(Action fn, string? ok = null, string? to = null) => ActTo(() =>
    {
        fn();
        return null;
    }, ok, to);

    protected IActionResult Err(int code, string message) => ErrorResult.Make(code, message);

    protected void Title(string t) => ViewData["Title"] = t;
}
