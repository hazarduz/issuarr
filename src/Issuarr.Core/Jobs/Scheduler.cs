using Issuarr.Core.Configuration;
using Issuarr.Core.Providers;
using Microsoft.Extensions.Hosting;

namespace Issuarr.Core.Jobs;

public class ScheduledTask
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public TimeSpan Interval { get; set; }
    public DateTime? LastRun { get; set; }
    public DateTime NextRun { get; set; }
}

public interface IScheduler
{
    IReadOnlyList<ScheduledTask> Tasks();
}

/// <summary>Queues recurring jobs: download processing every minute, RSS sync, and a metadata refresh twice a day.</summary>
public class Scheduler(ICommandService commands, IConfigService config, IIndexerRepository indexers, IDownloadClientRepository clients) : BackgroundService, IScheduler
{
    private readonly List<ScheduledTask> _tasks =
    [
        new() { Name = CommandNames.ProcessDownloads, Label = "Check download queue", Interval = TimeSpan.FromMinutes(1), NextRun = DateTime.UtcNow.AddSeconds(15) },
        new() { Name = CommandNames.RssSync, Label = "RSS Sync", Interval = TimeSpan.FromMinutes(15), NextRun = DateTime.UtcNow.AddMinutes(2) },
        new() { Name = CommandNames.RefreshAll, Label = "Refresh metadata", Interval = TimeSpan.FromHours(12), NextRun = DateTime.UtcNow.AddMinutes(10) },
    ];

    public IReadOnlyList<ScheduledTask> Tasks()
    {
        _tasks.First(t => t.Name == CommandNames.RssSync).Interval = TimeSpan.FromMinutes(config.RssSyncInterval);
        return _tasks;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var task in Tasks().Where(t => t.NextRun <= DateTime.UtcNow))
            {
                task.LastRun = DateTime.UtcNow;
                task.NextRun = DateTime.UtcNow + task.Interval;

                // Nothing to do without indexers / clients; don't fill the log with no-ops.
                if (task.Name == CommandNames.RssSync && !indexers.All().Any(i => i.Enable))
                {
                    continue;
                }

                if (task.Name == CommandNames.ProcessDownloads && !clients.All().Any(c => c.Enable))
                {
                    continue;
                }

                commands.Enqueue(task.Name, manual: false);
            }
        }
    }
}
