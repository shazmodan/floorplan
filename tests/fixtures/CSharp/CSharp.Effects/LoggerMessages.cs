using Microsoft.Extensions.Logging;

// Log calls through Microsoft.Extensions.Logging made with LoggerMessage.Define, and with [LoggerMessage] methods,
// whose code a source generator writes: the delegate Define returns is the log call, and so is a call to a
// generated method. They are grouped with the unit's other log calls, as LogInformation is.
namespace CSharp.Effects.Labels
{
    public partial class Printer(ILogger<Printer> log)
    {
        private static readonly Action<ILogger, string, Exception?> printing =
            LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, "Printing"), "Printing {Id}");

        public void Print(string id) => printing(log, id, null);

        public void Reprint(string id) => LogReprinting(log, id);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Reprinting {Id}")]
        private static partial void LogReprinting(ILogger logger, string id);
    }
}
