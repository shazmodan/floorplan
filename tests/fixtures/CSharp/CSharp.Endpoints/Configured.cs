// Routes named in appsettings.json: read by key, through a section, and through an options class bound to one.
using Microsoft.Extensions.Options;

namespace CSharp.Endpoints.Configured;

public class StatusOptions
{
    public string Path { get; set; } = "";
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
    }
}
