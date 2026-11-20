using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace CSharp.Wrappers;

/// A Serilog sink that appends each event to a file. Applications add it in code (WriteTo.Audit) or from
/// appsettings.json ("WriteTo": [{ "Name": "Audit" }]), where Serilog.Settings.Configuration calls the method by name.
public sealed class AuditSink(string path) : ILogEventSink
{
    public void Emit(LogEvent logEvent) => File.AppendAllText(path, logEvent.RenderMessage() + Environment.NewLine);
}

public static class AuditSinkExtensions
{
    public static LoggerConfiguration Audit(this LoggerSinkConfiguration writeTo, string path = "logs/audit.log") =>
        writeTo.Sink(new AuditSink(path));
}
