using System.Collections.Concurrent;
using Issuarr.Core.Decisions;
using Issuarr.Core.DownloadClients;
using Issuarr.Core.History;
using Issuarr.Core.Indexers;
using Issuarr.Core.Providers;
using Microsoft.Extensions.Logging;

namespace Issuarr.Core.Download;

public enum TrackedState
{
    Downloading,
    ImportPending,
    Importing,
    Imported,
    ImportFailed,
    Failed,
}

public class TrackedDownload
{
    public required DownloadItem Item { get; set; }
    public required ProviderDefinition Client { get; set; }
    public DownloadProtocol Protocol { get; set; }
    public int? TitleId { get; set; }
    public List<int> IssueIds { get; set; } = [];
    public string? Indexer { get; set; }
    public string SourceTitle { get; set; } = "";
    public TrackedState State { get; set; }
    public List<string> StatusMessages { get; set; } = [];
    public string Key => $"{Client.Id}:{Item.DownloadId}";
}

public interface ITrackedDownloadService : IPendingDownloads
{
    IReadOnlyCollection<TrackedDownload> All();
    TrackedDownload? Find(string key);
    Task Refresh(CancellationToken ct);
    void SetState(TrackedDownload download, TrackedState state, params string[] messages);
    void Forget(TrackedDownload download);
}

/// <summary>Polls download clients and links what they're doing back to the grabs Issuarr made.</summary>
public class TrackedDownloadService(
    IDownloadClientRepository clients,
    IEnumerable<IDownloadClient> impls,
    IHistoryService history,
    ILogger<TrackedDownloadService> log) : ITrackedDownloadService
{
    private readonly ConcurrentDictionary<string, TrackedDownload> _tracked = new();
    private readonly ConcurrentDictionary<int, string> _clientErrors = new();

    public IReadOnlyDictionary<int, string> ClientErrors => _clientErrors;

    public IReadOnlyCollection<TrackedDownload> All() => _tracked.Values.ToList();

    public TrackedDownload? Find(string key) => _tracked.GetValueOrDefault(key);

    public HashSet<int> PendingIssueIds() =>
        _tracked.Values.Where(t => t.State is not (TrackedState.Imported or TrackedState.Failed)).SelectMany(t => t.IssueIds).ToHashSet();

    public async Task Refresh(CancellationToken ct)
    {
        var seen = new HashSet<string>();
        foreach (var def in clients.All().Where(d => d.Enable))
        {
            var impl = impls.FirstOrDefault(i => i.Implementation == def.Implementation);
            if (impl == null)
            {
                continue;
            }

            List<DownloadItem> items;
            try
            {
                items = await impl.GetItems(def, ct);
                _clientErrors.TryRemove(def.Id, out _);
            }
            catch (Exception e) when (e is ProviderTestException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                _clientErrors[def.Id] = e.Message;
                log.LogWarning("Couldn't read queue from {Client}: {Message}", def.Name, e.Message);
                // Keep what we knew about this client's downloads rather than dropping them.
                foreach (var t in _tracked.Values.Where(t => t.Client.Id == def.Id))
                {
                    seen.Add(t.Key);
                }

                continue;
            }

            foreach (var item in items)
            {
                var key = $"{def.Id}:{item.DownloadId}";
                seen.Add(key);
                if (_tracked.TryGetValue(key, out var existing))
                {
                    existing.Item = item;
                    existing.Client = def;
                    if (existing.State == TrackedState.Downloading && item.Status == DownloadItemStatus.Completed)
                    {
                        existing.State = TrackedState.ImportPending;
                    }

                    continue;
                }

                var events = history.ForDownload(item.DownloadId);
                var grab = events.FirstOrDefault(e => e.EventType == HistoryEventType.Grabbed);
                var tracked = new TrackedDownload
                {
                    Item = item,
                    Client = def,
                    Protocol = impl.Protocol,
                    TitleId = grab?.TitleId,
                    IssueIds = grab?.Data.GetValueOrDefault("issueIds")?.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList() ?? [],
                    Indexer = grab?.Data.GetValueOrDefault("indexer"),
                    SourceTitle = grab?.SourceTitle ?? item.Title,
                    State = events.Any(e => e.EventType == HistoryEventType.Imported) ? TrackedState.Imported
                        : events.Any(e => e.EventType == HistoryEventType.DownloadFailed) ? TrackedState.Failed
                        : item.Status == DownloadItemStatus.Completed ? TrackedState.ImportPending
                        : TrackedState.Downloading,
                };

                if (grab == null)
                {
                    tracked.StatusMessages.Add("Not grabbed by Issuarr; it will be matched by name when it completes.");
                }

                _tracked[key] = tracked;
            }
        }

        foreach (var key in _tracked.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _tracked.TryRemove(key, out _);
        }
    }

    public void SetState(TrackedDownload download, TrackedState state, params string[] messages)
    {
        download.State = state;
        download.StatusMessages = messages.ToList();
    }

    public void Forget(TrackedDownload download) => _tracked.TryRemove(download.Key, out _);
}
