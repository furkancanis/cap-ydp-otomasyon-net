namespace CapYdp.Infrastructure;

/// <summary>
/// Türkiye saati (UTC+3, yaz saati uygulaması yok). Sunucu hangi saat diliminde olursa olsun
/// kayıtlar Türkiye saatiyle tutulur.
/// </summary>
public static class Clock
{
    public static DateTime Now() => DateTime.SpecifyKind(DateTime.UtcNow.AddHours(3), DateTimeKind.Unspecified);

    public static DateTime Today() => Now().Date;

    public static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
