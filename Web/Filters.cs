using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace CapYdp.Web;

public static class ErrorResult
{
    public static readonly Dictionary<int, string> Titles = new()
    {
        [400] = "Geçersiz istek", [403] = "Erişim engellendi", [404] = "Sayfa bulunamadı", [405] = "İzin verilmeyen yöntem",
        [413] = "Dosya çok büyük", [419] = "Oturum doğrulaması başarısız", [429] = "Çok fazla istek", [500] = "Sunucu hatası",
    };

    public static ViewResult Make(int code, string message)
    {
        var vd = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            ["Code"] = code,
            ["ErrTitle"] = Titles.TryGetValue(code, out var t) ? t : "Hata",
            ["Message"] = message,
            ["Title"] = Titles.TryGetValue(code, out var t2) ? t2 : "Hata",
        };
        return new ViewResult { ViewName = "~/Views/Shared/Error.cshtml", ViewData = vd, StatusCode = code };
    }
}

/// <summary>CSRF denetimini atlayan eylemler (yalnızca hata sayfası)</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipCsrfAttribute : Attribute
{
}

/// <summary>Tüm POST isteklerinde oturuma bağlı CSRF belirtecini doğrular (aksi 419)</summary>
public sealed class CsrfFilter : IAsyncAuthorizationFilter, IOrderedFilter
{
    public int Order => 10;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var req = context.HttpContext.Request;
        if (!HttpMethods.IsPost(req.Method))
        {
            return;
        }
        if (context.ActionDescriptor.EndpointMetadata.OfType<SkipCsrfAttribute>().Any())
        {
            return;
        }
        string? sent = null;
        if (req.HasFormContentType)
        {
            var form = await req.ReadFormAsync();
            sent = form["_csrf"].ToString();
        }
        if (string.IsNullOrEmpty(sent))
        {
            sent = req.Headers["X-CSRF-Token"].ToString();
        }
        var ctx = context.HttpContext.RequestServices.GetRequiredService<RequestCtx>();
        if (!ctx.CsrfValid(sent))
        {
            context.Result = ErrorResult.Make(419, "Güvenlik belirteci (CSRF) geçersiz veya süresi dolmuş. Sayfayı yenileyip tekrar deneyin.");
        }
    }
}

/// <summary>
/// Rol kontrolü: [Roles("koordinator", "bolum_baskani")]
/// Giriş yapılmamışsa giriş sayfasına, geçici şifre varsa şifre sayfasına yönlendirir; yetkisizse 403.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RolesAttribute : Attribute, IAuthorizationFilter, IOrderedFilter
{
    private readonly string[] _roles;

    public RolesAttribute(params string[] roles) => _roles = roles;

    public int Order => 0;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var ctx = context.HttpContext.RequestServices.GetRequiredService<RequestCtx>();
        var u = ctx.User;
        var path = context.HttpContext.Request.Path.Value ?? "/";
        if (u == null)
        {
            if (HttpMethods.IsGet(context.HttpContext.Request.Method))
            {
                ctx.Set("_intended", path);
            }
            context.Result = new RedirectResult("/giris");
            return;
        }
        if (u.MustChangePassword && path != "/sifre" && path != "/cikis")
        {
            ctx.AddFlash("info", "Devam etmeden önce geçici şifrenizi değiştirmeniz gerekiyor.");
            context.Result = new RedirectResult("/sifre");
            return;
        }
        if (_roles.Length > 0 && !_roles.Contains(u.Role))
        {
            context.Result = ErrorResult.Make(403, "Bu sayfaya erişim yetkiniz yok.");
        }
    }
}
