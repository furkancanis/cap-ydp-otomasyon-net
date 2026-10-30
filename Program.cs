using System.Text.Encodings.Web;
using System.Text.Unicode;
using CapYdp.Seed;
using CapYdp.Web;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.WebEncoders;

// =====================================================================
//  ÇAP / YDP Öğrenci Takip Otomasyonu — ASP.NET Core 8 MVC
//  Selçuk Üniversitesi Teknoloji Fakültesi Bilgisayar Mühendisliği
// =====================================================================

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);
// Yerel gizli ayarlar (Git'e girmez): appsettings.Local.json
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// Render / Railway PORT değişkenini verir
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Veritabanı: önce DATABASE_URL ortam değişkeni, yoksa appsettings.json -> ConnectionStrings:Default
var conn = Environment.GetEnvironmentVariable("DATABASE_URL");
if (string.IsNullOrWhiteSpace(conn))
{
    conn = builder.Configuration.GetConnectionString("Default");
}
Db.ConnectionString = DbUrl.ToConnectionString(conn ?? "");

const long maxBody = 20L * 1024 * 1024;
builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false;
    o.Limits.MaxRequestBodySize = maxBody;
});
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxBody);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<RequestCtx>();
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add<CsrfFilter>();
});
// Türkçe karakterler HTML'de &#x..; yerine olduğu gibi yazılsın
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

var app = builder.Build();

if (Environment.GetEnvironmentVariable("TRUST_PROXY") == "1")
{
    var fo = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
    fo.KnownNetworks.Clear();
    fo.KnownProxies.Clear();
    app.UseForwardedHeaders(fo);
}

// İlk açılışta tablolar kurulur; SEED_DEMO=0 değilse sentetik örnek veri yüklenir
try
{
    Seeder.InstallIfEmpty(app.Environment.ContentRootPath, Environment.GetEnvironmentVariable("SEED_DEMO") == "0");
}
catch (Exception ex)
{
    Console.Error.WriteLine("==========================================================");
    Console.Error.WriteLine(" Veritabanına bağlanılamadı veya kurulum yapılamadı.");
    Console.Error.WriteLine(" DATABASE_URL ortam değişkenini ya da appsettings.json içindeki");
    Console.Error.WriteLine(" ConnectionStrings:Default değerini kontrol edin.");
    Console.Error.WriteLine(" Hata: " + ex.Message);
    Console.Error.WriteLine("==========================================================");
    throw;
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/hata/500");
}
app.UseStatusCodePagesWithReExecute("/hata/{0}");

// Güvenlik başlıkları
app.Use(async (http, next) =>
{
    var h = http.Response.Headers;
    h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; connect-src 'self'; style-src 'self' https://fonts.googleapis.com; script-src 'self'; font-src 'self' https://fonts.gstatic.com; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "same-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    h["Cross-Origin-Opener-Policy"] = "same-origin";
    if (http.Request.IsHttps)
    {
        h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }
    await next();
});

app.UseStaticFiles();

// Veritabanında tutulan oturum: istek başında yüklenir, yanıt başlamadan kaydedilir
app.Use(async (http, next) =>
{
    var ctx = http.RequestServices.GetRequiredService<RequestCtx>();
    if (!http.Items.ContainsKey("cap_session"))
    {
        http.Items["cap_session"] = true;
        ctx.LoadSession();
        http.Response.OnStarting(() =>
        {
            ctx.SaveSession();
            return Task.CompletedTask;
        });
    }
    await next();
});

app.UseRouting();
app.MapControllers();
app.Run();
