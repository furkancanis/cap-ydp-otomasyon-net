using System.Text;
using System.Text.RegularExpressions;

namespace CapYdp.Services;

/// <summary>Denetimden geçmiş dosya</summary>
public sealed record CheckedFile(string Name, string Mime, long Size, byte[] Data, string Sha256);

/// <summary>
/// Yüklenen dosya denetimi:
///  - izin verilen uzantı + gerçek içerik türü (imza baytları) eşleşmesi
///  - boyut sınırı (ayarlardan)
///  - bozuk dosya kontrolü (PDF yapısı, PNG/JPEG yapısı)
///  - zararlı içerik kontrolü (PDF içinde JavaScript, otomatik çalıştırma, gömülü dosya;
///    çift uzantı; görüntü içine gömülü betik)
/// </summary>
public static class FileGuard
{
    public static readonly Dictionary<string, string> Types = new()
    {
        ["pdf"] = "application/pdf",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["png"] = "image/png",
    };

    private static readonly string[] Danger = { "php", "phtml", "phar", "exe", "js", "html", "htm", "svg", "bat", "cmd", "sh", "scr", "jar", "vbs", "ps1", "msi", "dll", "aspx", "asp", "cshtml" };

    public static CheckedFile Check(IAppCtx c, IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            throw new UserError(file == null ? "Dosya seçilmedi." : "Dosya boş.");
        }
        using var ms = new MemoryStream();
        file.CopyTo(ms);
        return Check(c, file.FileName, ms.ToArray());
    }

    public static CheckedFile Check(IAppCtx c, string fileName, byte[] data)
    {
        var maxMb = decimal.TryParse(Settings.Get(c, "max_upload_mb", "5"), out var m) ? m : 5m;
        if (data.Length == 0)
        {
            throw new UserError("Dosya boş.");
        }
        if (data.Length > maxMb * 1024 * 1024)
        {
            throw new UserError("Dosya en fazla " + H.Num(maxMb, 0) + " MB olabilir.");
        }

        var orig = Path.GetFileName((fileName ?? "dosya").Replace('\\', '/'));
        orig = Regex.Replace(orig, @"[^\p{L}\p{N}\.\-_ ()]", "_");
        if (orig.Length > 150)
        {
            orig = orig[^150..];
        }
        var parts = orig.ToLowerInvariant().Split('.');
        var ext = parts[^1];
        var allowed = Settings.Get(c, "allowed_extensions", "pdf,jpg,jpeg,png").Split(',').Select(x => x.Trim()).ToList();
        if (parts.Length < 2 || !Types.ContainsKey(ext) || !allowed.Contains(ext))
        {
            throw new UserError("İzin verilmeyen dosya türü. Yalnızca: " + string.Join(", ", allowed).ToUpperInvariant());
        }
        foreach (var p in parts.Skip(1).Take(parts.Length - 2))
        {
            if (Danger.Contains(p))
            {
                throw new UserError("Dosya adında şüpheli çift uzantı tespit edildi.");
            }
        }

        var expected = Types[ext];
        var detected = Detect(data);
        if (detected != expected)
        {
            throw new UserError("Dosyanın içeriği uzantısıyla uyuşmuyor (" + (detected ?? "bilinmeyen tür") + "). Dosya reddedildi.");
        }

        if (expected == "application/pdf")
        {
            CheckPdf(data);
        }
        else
        {
            if (expected == "image/png" ? !PngOk(data) : !JpegOk(data))
            {
                throw new UserError("Görüntü dosyası bozuk veya okunamıyor.");
            }
            var latin = Encoding.Latin1.GetString(data);
            if (Regex.IsMatch(latin, @"<\?php|<script|<\?=|eval\s*\(", RegexOptions.IgnoreCase))
            {
                throw new UserError("Görüntü dosyası içinde çalıştırılabilir kod tespit edildi.");
            }
        }
        return new CheckedFile(orig, expected, data.Length, data, Json.Sha256Hex(data));
    }

    private static string? Detect(byte[] d)
    {
        if (d.Length >= 5 && d[0] == '%' && d[1] == 'P' && d[2] == 'D' && d[3] == 'F' && d[4] == '-')
        {
            return "application/pdf";
        }
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47 && d[4] == 0x0D && d[5] == 0x0A && d[6] == 0x1A && d[7] == 0x0A)
        {
            return "image/png";
        }
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF)
        {
            return "image/jpeg";
        }
        if (d.Length >= 2 && d[0] == 'M' && d[1] == 'Z')
        {
            return "application/x-msdownload";
        }
        var head = Encoding.Latin1.GetString(d, 0, Math.Min(d.Length, 64)).TrimStart();
        if (head.StartsWith("<?php", StringComparison.OrdinalIgnoreCase))
        {
            return "text/x-php";
        }
        return null;
    }

    private static void CheckPdf(byte[] data)
    {
        var tail = Encoding.Latin1.GetString(data, Math.Max(0, data.Length - 2048), Math.Min(2048, data.Length));
        if (!tail.Contains("%%EOF"))
        {
            throw new UserError("PDF dosyası bozuk veya eksik yüklenmiş görünüyor.");
        }
        var text = Encoding.Latin1.GetString(data);
        // #xx kaçışlı yazımları da yakalamak için normalize et
        var norm = Regex.Replace(text, "#([0-9A-Fa-f]{2})", mm => ((char)Convert.ToInt32(mm.Groups[1].Value, 16)).ToString());
        foreach (var k in new[] { "/JavaScript", "/JS", "/Launch", "/EmbeddedFile", "/RichMedia", "/XFA", "/SubmitForm", "/ImportData" })
        {
            if (Regex.IsMatch(norm, Regex.Escape(k) + "(?![A-Za-z])"))
            {
                throw new UserError("PDF içinde güvenlik açısından izin verilmeyen içerik (" + k.TrimStart('/') + ") tespit edildi. Lütfen belgeyi yeniden tarayıp düz PDF olarak yükleyin.");
            }
        }
    }

    private static bool PngOk(byte[] d)
    {
        // İmza + IHDR parçası + IEND ile bitiş
        if (d.Length < 8 + 25 + 12)
        {
            return false;
        }
        if (Encoding.ASCII.GetString(d, 12, 4) != "IHDR")
        {
            return false;
        }
        var w = (d[16] << 24) | (d[17] << 16) | (d[18] << 8) | d[19];
        var h = (d[20] << 24) | (d[21] << 16) | (d[22] << 8) | d[23];
        if (w <= 0 || h <= 0 || (long)w * h > 60_000_000)
        {
            return false;
        }
        var end = Encoding.ASCII.GetString(d, d.Length - 8, 4);
        return end == "IEND";
    }

    private static bool JpegOk(byte[] d)
    {
        if (d.Length < 128 || d[^2] != 0xFF || d[^1] != 0xD9)
        {
            return false;
        }
        // SOF (çerçeve başlığı) işaretçisi bulunmalı
        for (var i = 2; i < d.Length - 9; i++)
        {
            if (d[i] == 0xFF && d[i + 1] >= 0xC0 && d[i + 1] <= 0xC3)
            {
                var h = (d[i + 5] << 8) | d[i + 6];
                var w = (d[i + 7] << 8) | d[i + 8];
                return h > 0 && w > 0;
            }
        }
        return false;
    }
}
