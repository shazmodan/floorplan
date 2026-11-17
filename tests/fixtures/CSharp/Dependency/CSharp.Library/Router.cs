using Microsoft.AspNetCore.Http;

namespace CSharp.Library;

/// The handlers an application passes in.
public sealed record Handlers(
    Func<HttpContext, Task> Health,
    Func<HttpContext, Task> CreateOrder,
    Func<HttpContext, string, Task> ShowOrder,
    Func<HttpContext, string, Task> RemoveOrder,
    Func<HttpContext, Task> NotFound);

/// The routing code of CSharp.Middleware, moved into a library the application depends on (plan section 9).
public static class Router
{
    public static Task Route(Handlers handlers, HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";
        var method = ctx.Request.Method;

        if (method == "GET" && path == "/health")
            return handlers.Health(ctx);

        if (method == "POST" && path.StartsWith("/orders"))
            return handlers.CreateOrder(ctx);

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 2 && segments[0] == "orders")
        {
            switch (method)
            {
                case "GET": return handlers.ShowOrder(ctx, segments[1]);
                case "DELETE": return handlers.RemoveOrder(ctx, segments[1]);
            }
        }

        return handlers.NotFound(ctx);
    }
}
