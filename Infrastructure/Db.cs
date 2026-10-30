using System.Text.RegularExpressions;
using Dapper;
using Npgsql;

namespace CapYdp.Infrastructure;

/// <summary>
/// Veritabanı erişim katmanı (PostgreSQL / Npgsql + Dapper).
/// Her HTTP isteği tek bir bağlantı kullanır; iç içe işlemler (transaction) desteklenir.
/// Tüm sorgular parametrelidir (SQL enjeksiyonuna karşı).
/// </summary>
public sealed class Db : IDisposable
{
    private static readonly Regex Ident = new("^[a-z_][a-z0-9_]*$", RegexOptions.Compiled);
    private readonly NpgsqlConnection _conn;
    private NpgsqlTransaction? _tx;
    private int _depth;

    public static string ConnectionString { get; set; } = "";

    static Db()
    {
        // Tarih/saatler "timestamp without time zone" olarak Türkiye saatiyle saklanır
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    public Db()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException("Veritabanı bağlantısı tanımlı değil. DATABASE_URL ortam değişkenini veya appsettings.json içindeki ConnectionStrings:Default değerini girin.");
        }
        _conn = new NpgsqlConnection(ConnectionString);
        _conn.Open();
    }

    public List<Row> All(string sql, object? param = null)
    {
        return _conn.Query(sql, param, _tx)
            .Select(r => new Row((IDictionary<string, object?>)(IDictionary<string, object>)r))
            .ToList();
    }

    public Row? One(string sql, object? param = null)
    {
        var r = _conn.QueryFirstOrDefault(sql, param, _tx);
        if (r == null)
        {
            return null;
        }
        return new Row((IDictionary<string, object?>)(IDictionary<string, object>)r);
    }

    public object? Scalar(string sql, object? param = null)
    {
        var v = _conn.ExecuteScalar(sql, param, _tx);
        return v is DBNull ? null : v;
    }

    public long Count(string sql, object? param = null)
    {
        var v = Scalar(sql, param);
        return v == null ? 0 : Convert.ToInt64(v);
    }

    public string? Str(string sql, object? param = null)
    {
        var v = Scalar(sql, param);
        return v == null ? null : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
    }

    public int Exec(string sql, object? param = null) => _conn.Execute(sql, param, _tx);

    /// <summary>INSERT ... RETURNING id</summary>
    public long Insert(string table, IDictionary<string, object?> data)
    {
        AssertIdent(table);
        var cols = data.Keys.ToList();
        cols.ForEach(AssertIdent);
        var sql = $"INSERT INTO {table} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", cols.Select(c => "@" + c))}) RETURNING id";
        var p = new DynamicParameters();
        foreach (var kv in data)
        {
            p.Add(kv.Key, kv.Value);
        }
        return Convert.ToInt64(_conn.ExecuteScalar(sql, p, _tx));
    }

    public void InsertNoId(string table, IDictionary<string, object?> data)
    {
        AssertIdent(table);
        var cols = data.Keys.ToList();
        cols.ForEach(AssertIdent);
        var sql = $"INSERT INTO {table} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", cols.Select(c => "@" + c))})";
        var p = new DynamicParameters();
        foreach (var kv in data)
        {
            p.Add(kv.Key, kv.Value);
        }
        _conn.Execute(sql, p, _tx);
    }

    /// <summary>UPDATE tablo SET ... WHERE (where ifadesindeki parametreler whereParams içinde verilir)</summary>
    public int Update(string table, IDictionary<string, object?> data, string where, object? whereParams = null)
    {
        AssertIdent(table);
        var p = new DynamicParameters(whereParams);
        var sets = new List<string>();
        foreach (var kv in data)
        {
            AssertIdent(kv.Key);
            sets.Add($"{kv.Key} = @set_{kv.Key}");
            p.Add("set_" + kv.Key, kv.Value);
        }
        return _conn.Execute($"UPDATE {table} SET {string.Join(", ", sets)} WHERE {where}", p, _tx);
    }

    public T Tx<T>(Func<T> fn)
    {
        if (_tx != null)
        {
            _depth++;
            try
            {
                return fn();
            }
            finally
            {
                _depth--;
            }
        }
        _tx = _conn.BeginTransaction();
        try
        {
            var r = fn();
            _tx.Commit();
            return r;
        }
        catch
        {
            try { _tx.Rollback(); } catch { /* yoksay */ }
            throw;
        }
        finally
        {
            _tx.Dispose();
            _tx = null;
        }
    }

    public void Tx(Action fn)
    {
        Tx<bool>(() =>
        {
            fn();
            return true;
        });
    }

    private static void AssertIdent(string s)
    {
        if (!Ident.IsMatch(s))
        {
            throw new ArgumentException("Geçersiz tanımlayıcı: " + s);
        }
    }

    public void Dispose()
    {
        _tx?.Dispose();
        _conn.Dispose();
    }

    // ------------------------------------------------------------------
    //  Kurulum
    // ------------------------------------------------------------------

    public bool IsInstalled()
    {
        var exists = Scalar("SELECT to_regclass('public.schema_version')::text");
        return exists != null;
    }

    public void Migrate(string contentRoot)
    {
        if (IsInstalled())
        {
            return;
        }
        var sql = File.ReadAllText(Path.Combine(contentRoot, "Data", "schema.sql"));
        Tx(() =>
        {
            using var cmd = new NpgsqlCommand(sql, _conn, _tx);
            cmd.ExecuteNonQuery();
        });
    }
}
