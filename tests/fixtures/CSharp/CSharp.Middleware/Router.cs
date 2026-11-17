namespace CSharp.Middleware;

/// Hand-written routing inside one ASP.NET Core middleware: conditions in control flow (form A), as C# writes
/// them: ==, StartsWith, the platform's HttpMethods helpers, Split with indexing, and a switch on the method.
public static class Router
{
    public static Task Route(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";
        var method = ctx.Request.Method;

        if (method == "GET" && path == "/health")
            return Handlers.Health(ctx);

        if (method == "POST" && path.StartsWith("/orders"))
            return Handlers.Create(ctx);

        if (HttpMethods.IsOptions(method) && path == "/orders")
            return Handlers.Options(ctx);

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 2 && segments[0] == "orders")
        {
            switch (method)
            {
                case "GET": return Handlers.Show(ctx, segments[1]);
                case "PUT": return Handlers.Replace(ctx, segments[1]);
                case "DELETE": return Handlers.Remove(ctx, segments[1]);
            }
        }

        return Handlers.NotFound(ctx);
    }
}

public static class Handlers
{
    public static Task Health(HttpContext ctx) => ctx.Response.WriteAsync("ok");

    public static Task Create(HttpContext ctx) => ctx.Response.WriteAsync("created");

    public static Task Options(HttpContext ctx) => ctx.Response.WriteAsync("GET, POST, PUT, DELETE");

    public static Task Show(HttpContext ctx, string id) => ctx.Response.WriteAsync($"order {id}");

    public static Task Replace(HttpContext ctx, string id) => ctx.Response.WriteAsync($"replaced {id}");

    public static Task Remove(HttpContext ctx, string id) => ctx.Response.WriteAsync($"deleted {id}");

    public static Task NotFound(HttpContext ctx)
    {
        ctx.Response.StatusCode = 404;
        return ctx.Response.WriteAsync("not found");
    }
}
