using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;

namespace CSharp.Effects;

/// Values kept between methods before they are used: in a dictionary a field holds, and in a memory cache.
public sealed class Cached(IMemoryCache cache)
{
    private static readonly HttpClient Client = new();

    private static readonly Dictionary<string, string> Queries = new()
    {
        ["stale"] = "DELETE FROM carts WHERE stale",
    };

    private readonly Dictionary<int, string> _byVersion = new();

    public Cached Register()
    {
        _byVersion[2] = "DELETE FROM carts WHERE version < 2";
        return this;
    }

    /// Read from a static field's dictionary filled where it is made.
    public int StaleCarts()
    {
        using var connection = new SqliteConnection("Data Source=app.db");
        using var command = connection.CreateCommand();
        command.CommandText = Queries["stale"];
        return command.ExecuteNonQuery();
    }

    /// Read from an instance field's dictionary another method fills, at an integer key.
    public int OldCarts()
    {
        using var connection = new SqliteConnection("Data Source=app.db");
        using var command = connection.CreateCommand();
        command.CommandText = _byVersion[2];
        return command.ExecuteNonQuery();
    }

    /// Stored in the cache by one method, read by another.
    public void Warm() => cache.Set("orders-url", "https://api.example.org/orders");

    public string Orders() => Client.GetStringAsync(cache.Get<string>("orders-url")).Result;

    /// Stored by the factory of the read itself.
    public string Status() => Client.GetStringAsync(cache.GetOrCreate("status-url", _ => "https://api.example.org/status")!).Result;

    /// A write at a key made at run time may be at the key read: what is there is not known.
    public void Remember(string id, string url) => cache.Set($"user:{id}", url);

    public string Me() => Client.GetStringAsync(cache.Get<string>("user:me")).Result;
}
