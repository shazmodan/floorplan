using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

// Log calls through Microsoft.Extensions.Logging, with Serilog as the provider: the calls reach Serilog's sinks
// through the logger factory, and are reported once for each unit that logs, as Serilog's own calls are.
namespace CSharp.Effects.Shipping
{
    public static class Setup
    {
        public static ServiceProvider Create() =>
            new ServiceCollection()
                .AddLogging(b => b.AddSerilog(new LoggerConfiguration().WriteTo.File("logs/shipping.log").CreateLogger()))
                .BuildServiceProvider();
    }

    public class Parcels(ILogger<Parcels> log)
    {
        public void Ship(string id)
        {
            log.LogInformation("Shipping {Id}", id);
            log.LogWarning("Late {Id}", id);
        }
    }
}
