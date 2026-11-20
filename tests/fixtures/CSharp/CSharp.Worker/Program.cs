// A worker: the host starts the hosted services it is given, and they do their work without a request.
using CSharp.Worker.Cleanup;
using CSharp.Worker.Monitoring;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Sweeper>();
builder.Services.AddHostedService(_ => new Heartbeat("https://status.example.org/ping"));
builder.Build().Run();

namespace CSharp.Worker.Cleanup
{
    // A BackgroundService: the host runs ExecuteAsync.
    public class Sweeper : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var file in Directory.GetFiles("tmp"))
                    File.Delete(file);

                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    // Never registered: not started by the host.
    public class Archiver : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => File.WriteAllTextAsync("archive.txt", "", stoppingToken);
    }
}

namespace CSharp.Worker.Monitoring
{
    // An IHostedService of its own: the host calls StartAsync and StopAsync.
    public class Heartbeat(string url) : IHostedService
    {
        private readonly HttpClient http = new();

        public Task StartAsync(CancellationToken cancellationToken) => http.GetAsync(url, cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken)
        {
            File.AppendAllText("heartbeat.log", "stopped");
            return Task.CompletedTask;
        }
    }
}
