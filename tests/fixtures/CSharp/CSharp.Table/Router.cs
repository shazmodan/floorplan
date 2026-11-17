namespace CSharp.Table;

public static class Router
{
    static readonly Dictionary<string, Func<string>> Table = new()
    {
        ["/orders"] = Handlers.Orders,
        ["/health"] = Handlers.Health,
    };

    public static string Route(string path) =>
        Table.TryGetValue(path, out var handler) ? handler() : "404";
}

public static class Handlers
{
    public static string Orders() => "orders";

    public static string Health() => "ok";
}
