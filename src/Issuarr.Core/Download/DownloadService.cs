using Issuarr.Core.DownloadClients;
using Issuarr.Core.History;
using Issuarr.Core.Indexers;
using Issuarr.Core.Issues;
using Issuarr.Core.Notifications;
using Issuarr.Core.Providers;
using Issuarr.Core.Titles;
using Microsoft.Extensions.Logging;

namespace Issuarr.Core.Download;

public interface IDownloadService
{
    Task<string> Grab(ReleaseInfo release, Title title, List<Issue> issues, CancellationToken ct);
}

/// <summary>Sends a release to the best enabled download client for its protocol and records the grab.</summary>
public class DownloadService(
    IDownloadClientRepository clients,
    IEnumerable<IDownloadClient> impls,
    IHistoryService history,
    INotificationService notifications,
    ILogger<DownloadService> log) : IDownloadService
{
    public async Task<string> Grab(ReleaseInfo release, Title title, List<Issue> issues, CancellationToken ct)
    {
        var (def, client) = clients.All()
            .Where(d => d.Enable)
            .Select(d => (Def: d, Impl: impls.FirstOrDefault(i => i.Implementation == d.Implementation)))
            .Where(x => x.Impl != null && x.Impl.Protocol == release.Protocol)
            .OrderBy(x => x.Def.Priority)
            .FirstOrDefault();

        if (client == null)
        {
            throw new InvalidOperationException($"No enabled {release.Protocol.ToString().ToLowerInvariant()} download client. Add one in Settings → Download Clients.");
        }

        var downloadId = await client.Add(def, release, ct);
        log.LogInformation("Grabbed {Release} for {Title} with {Client} ({Id})", release.Title, title.Name, def.Name, downloadId);

        foreach (var issue in issues.DefaultIfEmpty())
        {
            history.Add(new HistoryItem
            {
                TitleId = title.Id,
                IssueId = issue?.Id,
                EventType = HistoryEventType.Grabbed,
                SourceTitle = release.Title,
                DownloadId = downloadId,
                Data = new Dictionary<string, string>
                {
                    ["indexer"] = release.Indexer,
                    ["size"] = release.Size.ToString(),
                    ["protocol"] = release.Protocol.ToString(),
                    ["downloadClient"] = def.Name,
                    ["downloadClientId"] = def.Id.ToString(),
                    ["issueIds"] = string.Join(",", issues.Select(i => i.Id)),
                    ["guid"] = release.Guid,
                },
            });
        }

        notifications.Grabbed(new GrabMessage(title, issues, release.Title, release.Indexer, def.Name, release.Size));
        return downloadId;
    }
}
