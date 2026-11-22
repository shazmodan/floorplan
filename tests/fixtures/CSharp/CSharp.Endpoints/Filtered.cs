// Route groups with endpoint filters: a filter runs before the handler of each endpoint it applies to, on a group
// (every endpoint in it, nested groups too) or on one endpoint, as a lambda or as an IEndpointFilter type.
namespace CSharp.Endpoints.Filtered;

public static class Routes
{
    public static void Map(WebApplication app)
    {
        // A lambda filter on a group, chained before the group's routes.
        var v1 = app.MapGroup("/v1").AddEndpointFilter(async (context, next) =>
        {
            File.AppendAllText("requests.log", context.HttpContext.Request.Path);
            return await next(context);
        });

        v1.MapGet("/items", () => "items");
        v1.MapGroup("/stock").MapGet("/{sku}", (string sku) => sku);

        // A filter type on one endpoint.
        app.MapGet("/v1/prices", () => "prices").AddEndpointFilter<Throttle>();

        // A filter type on a group, with other conventions in the chain.
        var v2 = app.MapGroup("/v2").WithTags("v2").AddEndpointFilter<Throttle>().RequireAuthorization();
        v2.MapPost("/orders", () => "ordered");

        // A filter factory on a group.
        app.MapGroup("/v3")
            .AddEndpointFilterFactory((factory, next) => context =>
            {
                File.AppendAllText("factory.log", "v3");
                return next(context);
            })
            .MapGet("/ping", () => "pong");
    }
}

public class Throttle : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var limit = File.ReadAllText("limits.json");
        return limit == "0" ? Results.StatusCode(429) : await next(context);
    }
}
