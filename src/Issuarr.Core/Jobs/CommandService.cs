using System.Collections.Concurrent;
using System.Threading.Channels;
using Issuarr.Core.Download;
using Issuarr.Core.IndexerSearch;
using Issuarr.Core.Titles;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Issuarr.Core.Jobs;

public enum CommandStatus
{
    Queued,
    Started,
    Completed,
    Failed,
}

public class Command
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? TitleId { get; set; }
    public List<int>? IssueIds { get; set; }
    public CommandStatus Status { get; set; }
    public string? Message { get; set; }
    public DateTime Queued { get; set; }
    public DateTime? Started { get; set; }
    public DateTime? Ended { get; set; }
    public bool Manual { get; set; }

    public string Key => $"{Name}|{TitleId}|{(IssueIds == null ? "" : string.Join(",", IssueIds.Order()))}";
}

public static class CommandNames
{
    public const string RefreshTitle = "RefreshTitle";
    public const string RefreshAll = "RefreshAll";
    public const string TitleSearch = "TitleSearch";
    public const string IssueSearch = "IssueSearch";
    public const string MissingSearch = "MissingSearch";
    public const string RssSync = "RssSync";
    public const string ProcessDownloads = "ProcessDownloads";
    public const string RenameTitle = "RenameTitle";

    public static readonly string[] All = [RefreshTitle, RefreshAll, TitleSearch, IssueSearch, MissingSearch, RssSync, ProcessDownloads, RenameTitle];
}

public interface ICommandService
{
    Command Enqueue(string name, int? titleId = null, List<int>? issueIds = null, bool manual = true);
    IReadOnlyList<Command> Recent();
    Command? Get(int id);
}

/// <summary>
/// Runs long jobs (searches, refreshes, RSS sync, download processing) in the
/// background so API calls return straight away. Up to three run at once, and a
/// job that's already queued or running isn't queued twice.
/// </summary>
public class CommandService(IServiceProvider services, ILogger<CommandService> log) : BackgroundService, ICommandService
{
    private readonly Channel<Command> _channel = Channel.CreateUnbounded<Command>();
    private readonly ConcurrentDictionary<int, Command> _commands = new();
    private readonly SemaphoreSlim _slots = new(3);
    private int _nextId;

    public Command Enqueue(string name, int? titleId = null, List<int>? issueIds = null, bool manual = true)
    {
        if (!CommandNames.All.Contains(name))
        {
            throw new ArgumentException($"Unknown command: {name}");
        }

        var cmd = new Command { Name = name, TitleId = titleId, IssueIds = issueIds, Queued = DateTime.UtcNow, Manual = manual };
        var duplicate = _commands.Values.FirstOrDefault(c => c.Key == cmd.Key && c.Status is CommandStatus.Queued or CommandStatus.Started);
        if (duplicate != null)
        {
            return duplicate;
        }

        cmd.Id = Interlocked.Increment(ref _nextId);
        _commands[cmd.Id] = cmd;
        _channel.Writer.TryWrite(cmd);

        // Keep the last 200 commands.
        foreach (var old in _commands.Values.Where(c => c.Status is CommandStatus.Completed or CommandStatus.Failed).OrderByDescending(c => c.Id).Skip(200))
        {
            _commands.TryRemove(old.Id, out _);
        }

        return cmd;
    }

    public IReadOnlyList<Command> Recent() => _commands.Values.OrderByDescending(c => c.Id).ToList();

    public Command? Get(int id) => _commands.GetValueOrDefault(id);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var cmd in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            await _slots.WaitAsync(stoppingToken);
            _ = Task.Run(async () =>
            {
                try
                {
                    await Run(cmd, stoppingToken);
                }
                finally
                {
                    _slots.Release();
                }
            }, stoppingToken);
        }
    }

    private async Task Run(Command cmd, CancellationToken ct)
    {
        cmd.Status = CommandStatus.Started;
        cmd.Started = DateTime.UtcNow;
        try
        {
            cmd.Message = await Execute(cmd, ct);
            cmd.Status = CommandStatus.Completed;
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            cmd.Status = CommandStatus.Failed;
            cmd.Message = e.Message;
            log.LogWarning("Command {Name} failed: {Message}", cmd.Name, e.Message);
        }
        finally
        {
            cmd.Ended = DateTime.UtcNow;
        }
    }

    private async Task<string> Execute(Command cmd, CancellationToken ct)
    {
        T Get<T>() where T : notnull => (T)services.GetService(typeof(T))!;

        switch (cmd.Name)
        {
            case CommandNames.RefreshTitle:
                var refreshId = cmd.TitleId ?? throw new ArgumentException("titleId is required");
                var added = await Get<IRefreshTitleService>().Refresh(refreshId, ct);
                var scan = Get<MediaFiles.IDiskScanService>().Scan(refreshId);
                var metadata = added == 0 ? "Up to date" : $"{added} new issue{(added == 1 ? "" : "s")}";
                return scan.Added == 0 && scan.Removed == 0 && scan.FolderExists ? metadata : $"{metadata} · {scan.Summary}";

            case CommandNames.RefreshAll:
                await Get<IRefreshTitleService>().RefreshAll(ct);
                return "Library refreshed";

            case CommandNames.TitleSearch:
                return Grabbed(await Get<ISearchService>().Automatic(cmd.TitleId ?? throw new ArgumentException("titleId is required"), null, ct));

            case CommandNames.IssueSearch:
                if (cmd.TitleId is not { } tid || cmd.IssueIds is not { Count: > 0 } ids)
                {
                    throw new ArgumentException("titleId and issueIds are required");
                }

                return Grabbed(await Get<ISearchService>().Automatic(tid, ids, ct));

            case CommandNames.MissingSearch:
                return Grabbed(await Get<ISearchService>().MissingSearch(ct));

            case CommandNames.RssSync:
                return Grabbed(await Get<ISearchService>().RssSync(ct));

            case CommandNames.ProcessDownloads:
                await Get<ITrackedDownloadService>().Refresh(ct);
                foreach (var (titleId, issueIds) in await Get<ICompletedDownloadService>().Process(ct))
                {
                    Enqueue(CommandNames.IssueSearch, titleId, issueIds, manual: false);
                }

                return "Queue checked";

            case CommandNames.RenameTitle:
                var renamed = Get<ITitleManagementService>().Rename(cmd.TitleId ?? throw new ArgumentException("titleId is required"), cmd.IssueIds);
                return $"{renamed} file{(renamed == 1 ? "" : "s")} renamed";

            default:
                throw new ArgumentException($"Unknown command: {cmd.Name}");
        }
    }

    private static string Grabbed(int n) => n == 0 ? "Nothing new to grab" : $"Grabbed {n} release{(n == 1 ? "" : "s")}";
}
