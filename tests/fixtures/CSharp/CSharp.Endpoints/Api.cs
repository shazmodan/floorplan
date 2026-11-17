namespace CSharp.Endpoints;

public static class Api
{
    public static string ListOrders() => "orders";

    /// Payloads built as C# writes them: an interpolated string and a composite format string.
    public static string GetOrder(int id) => File.ReadAllText($"orders/{id}.json");

    public static string CreateOrder()
    {
        File.WriteAllText(string.Format("orders/{0}.json", Guid.NewGuid()), "{}");
        return "created";
    }

    public static async Task<string> DeleteOrderAsync(int id)
    {
        await Task.Yield();
        return $"deleted {id}";
    }
}

/// Routes added to a group that the caller created, with a nested group and a convention chain.
public static class Admin
{
    public static void Map(RouteGroupBuilder admin)
    {
        admin.MapGet("/users", () => "users");
        admin.MapGroup("/reports").MapPost("/rebuild", () => "rebuilding").RequireAuthorization();
    }
}
