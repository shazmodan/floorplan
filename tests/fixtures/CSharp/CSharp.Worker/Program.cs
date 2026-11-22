// A worker: the host starts the hosted services it is given, and they do their work without a request.
using CSharp.Worker.Cleanup;
using CSharp.Worker.Monitoring;
using Quartz;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Sweeper>();
builder.Services.AddHostedService(_ => new Heartbeat("https://status.example.org/ping"));
builder.Services.AddQuartz(q => q.ScheduleJob<CSharp.Worker.Cleanup.PurgeJob>(t => t.WithCronSchedule("0 0 3 * * ?")));
builder.Services.AddQuartzHostedService();
CSharp.Worker.Imports.Inbox.Watch();
CSharp.Worker.Reporting.Schedule.Jobs();
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

    // A Quartz job: the scheduler runs Execute when its trigger fires.
    public class PurgeJob : Quartz.IJob
    {
        public Task Execute(Quartz.IJobExecutionContext context)
        {
            Directory.Delete("cache", recursive: true);
            return Task.CompletedTask;
        }
    }

    // Never registered: not started by the host.
    public class Archiver : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => File.WriteAllTextAsync("archive.txt", "", stoppingToken);
    }
}

namespace CSharp.Worker.Reporting
{
    // Hangfire: a job is an expression tree, and its server runs the method the tree calls; a recurring job runs on
    // its cron expression.
    public class Reports
    {
        public void Nightly() => File.WriteAllText("nightly.txt", DateTime.UtcNow.ToString("O"));

        public void Weekly() => File.WriteAllText("weekly.txt", "");

        public void Hourly() => File.WriteAllText("hourly.txt", "");

        public Task Send(string to) => File.AppendAllTextAsync("outbox.log", to);
    }

    public static class Schedule
    {
        public static void Jobs()
        {
            // A schedule from Hangfire's Cron helpers, a literal one with a queue, and a helper the server calls.
            Hangfire.RecurringJob.AddOrUpdate<Reports>("nightly", r => r.Nightly(), Hangfire.Cron.Daily());
            Hangfire.RecurringJob.AddOrUpdate<Reports>("weekly", "reports", r => r.Weekly(), "0 6 * * 1");
            Hangfire.RecurringJob.AddOrUpdate<Reports>("hourly", r => r.Hourly(), Hangfire.Cron.Hourly);
            Hangfire.BackgroundJob.Enqueue<Reports>(r => r.Send("ops@example.org"));
        }
    }
}

namespace CSharp.Worker.Imports
{
    // A watcher calls its handlers when a file in the path it watches changes: an input from the file system.
    public static class Inbox
    {
        public static FileSystemWatcher Watch()
        {
            var watcher = new FileSystemWatcher("inbox");
            watcher.Created += (_, e) => File.Copy(e.FullPath, Path.Combine("imported", e.Name!));
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
    }
}

namespace CSharp.Worker.Monitoring
{
    // An IHostedService of its own: the host calls StartAsync and StopAsync.
    public class Heartbeat(string url) : IHostedService
    {
        private readonly HttpClient http = new();

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Timers call back each time they fire: background work of their own.
            var flush = new System.Threading.Timer(_ => File.AppendAllText("heartbeat.log", "beat"), null, 0, 60_000);
            var audit = new System.Timers.Timer(300_000);
            audit.Elapsed += OnAudit;
            audit.Start();
            return http.GetAsync(url, cancellationToken);
        }

        private static void OnAudit(object? sender, System.Timers.ElapsedEventArgs e) => File.WriteAllText("audit.txt", e.SignalTime.ToString("O"));

        public Task StopAsync(CancellationToken cancellationToken)
        {
            File.AppendAllText("heartbeat.log", "stopped");
            return Task.CompletedTask;
        }
    }
}
