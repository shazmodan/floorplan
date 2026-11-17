namespace CSharp.Delegated;

public static class Orders
{
    public static Task Health(HttpContext ctx) => ctx.Response.WriteAsync("ok");

    public static Task Create(HttpContext ctx) => ctx.Response.WriteAsync("created");

    public static Task Show(HttpContext ctx, string id) => ctx.Response.WriteAsync($"order {id}");

    public static Task Remove(HttpContext ctx, string id) => ctx.Response.WriteAsync($"deleted {id}");

    public static Task NotFound(HttpContext ctx)
    {
        ctx.Response.StatusCode = 404;
        return ctx.Response.WriteAsync("not found");
    }
}
