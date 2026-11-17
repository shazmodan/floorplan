using Microsoft.Extensions.DependencyInjection;

namespace CSharp.Effects;

/// Effects behind delegates, interfaces, dependency injection, async methods and iterators.
public static class HigherOrder
{
    /// Takes the effect as a delegate; has none itself.
    public static void Process(Action<string> save, IEnumerable<string> items)
    {
        foreach (var item in items) save(item);
    }

    /// Supplies the effect as a lambda that captures a local: the file write is in this class.
    public static void SaveAll(IEnumerable<string> items, string file)
    {
        Process(item => File.AppendAllText(file, item), items);
    }

    public static void Store(IStore store) => store.Save("hello");

    public static void UseStore() => Store(new FileStore());

    /// The implementation is only named in a container registration and resolved through the interface.
    public static void UseContainer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStore, AuditStore>();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStore>().Save("audit");
    }

    public static async Task<int> FetchAsync(HttpClient client)
    {
        var body = await client.GetStringAsync("https://api.example.org/async");
        return body.Length;
    }

    public static IEnumerable<string> ReadLines()
    {
        foreach (var line in File.ReadLines("lines.txt"))
            yield return line.Trim();
    }
}

public interface IStore
{
    void Save(string text);
}

public class FileStore : IStore
{
    public void Save(string text) => File.WriteAllText("store.txt", text);
}

public class AuditStore : IStore
{
    public void Save(string text) => File.AppendAllText("audit.txt", text);
}
