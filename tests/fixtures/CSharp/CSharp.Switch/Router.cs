namespace CSharp.Switch;

public static class Router
{
    public static string Route(string method, string path) =>
        (method, path) switch
        {
            ("GET", "/api/orders") => Orders.List(),
            ("POST", "/api/orders") => Orders.Create(),
            ("GET", "/api/customers") => Orders.Customers(),
            ("GET", "/api/invoices") => Orders.Invoices(),
            ("GET", "/api/products") => Orders.Products(),
            ("GET", "/api/suppliers") => Orders.Suppliers(),
            ("GET", "/api/stock") => Orders.Stock(),
            _ => BySegments(method, path.Split('/', StringSplitOptions.RemoveEmptyEntries)),
        };

    /// List patterns over path segments, with a segment taken as a parameter.
    static string BySegments(string method, string[] segments) =>
        (method, segments) switch
        {
            ("GET", ["api", "orders", var id]) => Orders.Fetch(id),
            ("DELETE", ["api", "orders", var id]) => Orders.Remove(id),
            _ => "404",
        };
}

public static class Orders
{
    public static string List() => "orders";

    public static string Create() => "created";

    public static string Customers() => "customers";

    public static string Invoices() => "invoices";

    public static string Products() => "products";

    public static string Suppliers() => "suppliers";

    public static string Stock() => "stock";

    public static string Fetch(string id) => $"order {id}";

    public static string Remove(string id) => $"deleted {id}";
}
