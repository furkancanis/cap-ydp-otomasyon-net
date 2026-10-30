using System.Globalization;

namespace CapYdp.Infrastructure;

/// <summary>
/// Sorgu sonucu satırı. Dapper'ın döndürdüğü sütun/değer sözlüğünü
/// tip güvenli okuma metotlarıyla sarar (r.S("full_name"), r.L("id") ...).
/// </summary>
public sealed class Row
{
    private readonly IDictionary<string, object?> _d;

    public Row(IDictionary<string, object?> d)
    {
        _d = d;
    }

    public object? this[string key]
    {
        get
        {
            if (_d.TryGetValue(key, out var v) && v is not DBNull)
            {
                return v;
            }
            return null;
        }
    }

    public bool IsNull(string key) => this[key] == null;

    /// <summary>Metin olarak (null ise boş metin)</summary>
    public string S(string key)
    {
        var v = this[key];
        return v switch
        {
            null => "",
            string s => s,
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
        };
    }

    /// <summary>Metin ya da null</summary>
    public string? Sn(string key) => this[key] == null ? null : S(key);

    public long L(string key)
    {
        var v = this[key];
        return v == null ? 0 : Convert.ToInt64(v, CultureInfo.InvariantCulture);
    }

    public long? Ln(string key) => this[key] == null ? null : L(key);

    public int I(string key) => (int)L(key);

    public bool B(string key) => L(key) != 0;

    public decimal Dec(string key)
    {
        var v = this[key];
        return v == null ? 0m : Convert.ToDecimal(v, CultureInfo.InvariantCulture);
    }

    public decimal? DecN(string key) => this[key] == null ? null : Dec(key);

    public DateTime? Dt(string key)
    {
        var v = this[key];
        return v switch
        {
            null => null,
            DateTime dt => dt,
            string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var p) => p,
            _ => null
        };
    }

    public IEnumerable<string> Keys => _d.Keys;

    /// <summary>Denetim kaydı için sözlük kopyası</summary>
    public Dictionary<string, object?> ToDict()
    {
        var r = new Dictionary<string, object?>();
        foreach (var kv in _d)
        {
            r[kv.Key] = kv.Value is DBNull ? null : kv.Value;
        }
        return r;
    }
}
