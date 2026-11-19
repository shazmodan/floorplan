using Serilog;

namespace CSharp.Endpoints.Auditing;

/// Log calls behind a route. Started logs first in the file, so the unit's logging exit points are shown at it; the
/// route reaches only Cancelled, and its trace still has them.
public static class Audit
{
    private static readonly Serilog.ILogger Log = new LoggerConfiguration().WriteTo.File("logs/audit.log").CreateLogger();

    public static void Started() => Log.Information("Started");

    public static string Cancelled()
    {
        Log.Warning("Cancelled");
        return "ok";
    }
}
