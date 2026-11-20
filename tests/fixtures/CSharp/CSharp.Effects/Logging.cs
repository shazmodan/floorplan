using Serilog;

// Log calls. The sinks the logger is configured with write files and the console, and every
// log call reaches them; what they do is reported once for each unit that logs, not at every call.
namespace CSharp.Effects.Logging
{
    public static class Setup
    {
        // Configuration: these calls are the application's own and stay one exit point each.
        public static Serilog.ILogger Create() =>
            new LoggerConfiguration().WriteTo.File("logs/app.log").WriteTo.Console().CreateLogger();
    }

    public class Orders(Serilog.ILogger log)
    {
        public void Place(string id)
        {
            log.Information("Placing order {Id}", id);
            log.Debug("Checked stock for {Id}", id);
        }

        public void Cancel(string id) => log.Warning("Cancelled {Id}", id);

        public static void Shutdown() => Log.Fatal("Stopping");
    }
}

namespace CSharp.Effects.Billing
{
    public class Invoices(Serilog.ILogger log)
    {
        public void Send(string id) => log.Error("Could not send {Id}", id);
    }

    // Loggers made for a context: making one is not a log call, and logging through it is.
    public class Refunds(Serilog.ILogger log)
    {
        private readonly Serilog.ILogger forType = Log.ForContext<Refunds>();

        public void Refund(string id)
        {
            var scoped = log.ForContext("RefundId", id);
            scoped.Information("Refunding");
            forType.Warning("Refunded {Id}", id);
        }
    }
}
