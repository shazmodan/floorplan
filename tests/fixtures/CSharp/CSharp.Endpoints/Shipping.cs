using Serilog;

// Log calls through Microsoft.Extensions.Logging in ASP.NET Core, where the logging library is part of the shared
// framework and its code is not followed: the calls still reach the sinks of the Serilog provider it is given.
namespace CSharp.Endpoints.Shipping
{
    public static class Setup
    {
        public static WebApplicationBuilder Configure(WebApplicationBuilder builder)
        {
            builder.Logging.AddSerilog(new LoggerConfiguration().WriteTo.File("logs/shipping.log").CreateLogger());
            return builder;
        }
    }

    public class Parcels(ILogger<Parcels> log)
    {
        public void Ship(string id) => log.LogInformation("Shipping {Id}", id);
    }
}
