using log4net.Config;
using Microsoft.Extensions.Configuration;
using NLog.Extensions.Logging;
using Serilog;

// Loggers configured from files, each loaded by a call: what the sinks, targets and appenders the files name do
// when they are made (opening a file) happens at that call.
namespace CSharp.Effects.Loading
{
    public static class Setup
    {
        public static Serilog.ILogger FromSettings(IConfiguration settings) =>
            new LoggerConfiguration().ReadFrom.Configuration(settings).CreateLogger();

        public static void NLogFromSettings(IConfiguration settings) =>
            NLog.LogManager.Setup().LoadConfigurationFromSection(settings);

        public static void Log4NetFromFile() => XmlConfigurator.Configure(new FileInfo("log4net.config"));
    }
}
