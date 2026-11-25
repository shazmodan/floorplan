// Routes named in appsettings.json: read by key, through a section, and through an options class bound to one
// (its own properties and a nested object's).
using Microsoft.Extensions.Options;

namespace CSharp.Endpoints.Configured;

public class StatusOptions
{
    public string Path { get; set; } = "";
    public LinkOptions Links { get; set; } = new();
}

public class LinkOptions
{
    public string Docs { get; set; } = "";
}

public static class Routes
{
    public static void Configure(IServiceCollection services, IConfiguration configuration) =>
        services.Configure<StatusOptions>(configuration.GetSection("Status"));

    public static void Map(WebApplication app)
    {
        app.MapGet(app.Configuration["Routes:Health"]!, () => "healthy");

        var routes = app.Configuration.GetSection("Routes");
        app.MapGet(routes["Version"]!, () => "1.0");

        var status = app.Services.GetRequiredService<IOptions<StatusOptions>>().Value;
        app.MapGet(status.Path, () => "up");

        // A property of a nested options object.
        app.MapGet(status.Links.Docs, () => "docs");
    }
}
