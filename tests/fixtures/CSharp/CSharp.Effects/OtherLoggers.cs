using log4net.Appender;
using log4net.Config;
using log4net.Layout;
using NLog.Config;
using NLog.Targets;

// Log calls through NLog and log4net, each configured in code with a file: what the file does is reported once
// for each unit that logs, as Serilog's sinks are.
namespace CSharp.Effects.Packing
{
    public static class Setup
    {
        public static void Configure()
        {
            var config = new LoggingConfiguration();
            config.AddRuleForAllLevels(new FileTarget("file") { FileName = "logs/packing.log" });
            NLog.LogManager.Configuration = config;
        }
    }

    public class Boxes
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

        public void Pack(string id)
        {
            log.Info("Packing {Id}", id);
            log.Warn("Heavy {Id}", id);
        }
    }
}

namespace CSharp.Effects.Storage
{
    public static class Setup
    {
        public static void Configure() =>
            BasicConfigurator.Configure(new FileAppender { File = "logs/storage.log", Layout = new SimpleLayout() });
    }

    public class Shelves
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof(Shelves));

        public void Store(string id)
        {
            log.Info($"Storing {id}");
            log.WarnFormat("Full {0}", id);
        }
    }
}
