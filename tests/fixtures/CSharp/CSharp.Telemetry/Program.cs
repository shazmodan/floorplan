using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://collector:4317"))
        .AddZipkinExporter(o => o.Endpoint = new Uri("http://zipkin:9411/api/v2/spans"))
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter((exporter, reader) =>
        {
            exporter.Endpoint = new Uri("http://collector:4318/v1/metrics");
            exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
        })
        .AddPrometheusExporter());

// No endpoint: OTEL_EXPORTER_OTLP_ENDPOINT's, or the default.
builder.Logging.AddOpenTelemetry(logging => logging.AddOtlpExporter());

builder.Services.AddOpenTelemetry().UseOtlpExporter(OtlpExportProtocol.Grpc, new Uri("http://agent:4317"));

builder.Services.AddOpenTelemetry().UseAzureMonitor(o => o.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000");

var app = builder.Build();

// Scraped by Prometheus: no exit point.
app.MapPrometheusScrapingEndpoint();

app.MapGet("/hello", () => "hello");

app.Run();
