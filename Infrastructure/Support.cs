using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Npgsql;

namespace CapYdp.Infrastructure;

/// <summary>postgres://kullanici:sifre@host:5432/db?sslmode=require  ->  Npgsql bağlantı metni</summary>
public static class DbUrl
{
    public static string ToConnectionString(string value)
    {
        value = value.Trim();
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return value; // zaten Npgsql biçiminde
        }
        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);
        var b = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            MaxPoolSize = 20,
            Timeout = 30,
        };
        var query = uri.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
            {
                b.SslMode = kv[1].ToLowerInvariant() switch
                {
                    "disable" => SslMode.Disable,
                    "allow" => SslMode.Allow,
                    "prefer" => SslMode.Prefer,
                    "verify-ca" => SslMode.VerifyCA,
                    "verify-full" => SslMode.VerifyFull,
                    _ => SslMode.Require,
                };
            }
        }
        return b.ConnectionString;
    }
}

/// <summary>PBKDF2-SHA256 parola özeti (210.000 tur, rastgele tuz). Ek paket gerektirmez.</summary>
public static class Passwords
{
    private const int Iterations = 210_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        try
        {
            var p = stored.Split('$');
            if (p.Length != 4 || p[0] != "pbkdf2")
            {
                return false;
            }
            var iter = int.Parse(p[1]);
            var salt = Convert.FromBase64String(p[2]);
            var expected = Convert.FromBase64String(p[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iter, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Kullanıcı bulunamadığında da aynı süre harcansın diye kullanılan sahte özet</summary>
    public static readonly string Dummy = Hash("sahte-parola-zamanlama");

    public static string Random(int len = 12)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        var sb = new StringBuilder();
        for (var i = 0; i < len - 2; i++)
        {
            sb.Append(chars[RandomNumberGenerator.GetInt32(chars.Length)]);
        }
        sb.Append(RandomNumberGenerator.GetInt32(10, 100));
        return sb.ToString();
    }

    public static string? PolicyError(string pw)
    {
        if (pw.Length < 8)
        {
            return "Şifre en az 8 karakter olmalıdır.";
        }
        if (!pw.Any(char.IsLetter) || !pw.Any(char.IsDigit))
        {
            return "Şifre en az bir harf ve bir rakam içermelidir.";
        }
        if (pw.Length > 128)
        {
            return "Şifre çok uzun.";
        }
        return null;
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public static string Encode(object? value) => JsonSerializer.Serialize(value, Options);

    public static T? Decode<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch
        {
            return default;
        }
    }

    public static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static string Sha256Hex(string s) => Sha256Hex(Encoding.UTF8.GetBytes(s));
}
