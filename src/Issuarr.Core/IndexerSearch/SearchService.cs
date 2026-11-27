using Issuarr.Core.Decisions;
using Issuarr.Core.Download;
using Issuarr.Core.Indexers;
using Issuarr.Core.Issues;
using Issuarr.Core.Providers;
using Issuarr.Core.Titles;
using Microsoft.Extensions.Logging;

namespace Issuarr.Core.IndexerSearch;

public interface ISearchService
{
    /// <summary>Searches indexers for a title and returns every release with its decision (for interactive search).</summary>
    Task<List<DownloadDecision>> Interactive(int titleId, int? issueId, CancellationToken ct);

    /// <summary>Searches and grabs the best release for each wanted issue. Returns how many were grabbed.</summary>
    Task<int> Automatic(int titleId, IReadOnlyCollection<int>? issueIds, CancellationToken ct);

    Task<int> MissingSearch(CancellationToken ct);
    Task<int> RssSync(CancellationToken ct);
    Task<string> GrabRelease(string guid, int titleId, int? issueId, CancellationToken ct);
}

public class SearchService(
    IIndexerRepository indexerRepo,
    IEnumerable<IIndexer> indexerImpls,
    IDecisionMaker decisions,
    IDownloadService downloads,
    ITitleService titles,
    IIssueRepository issues,
    IClock clock,
    ILogger<SearchService> log) : ISearchService
{
    // Interactive results are cached briefly so "grab" can find the release the user clicked.
    private static readonly Dictionary<string, (ReleaseInfo Release, DateTime At)> RecentReleases = new();
    private static readonly object CacheLock = new();

    public async Task<List<DownloadDecision>> Interactive(int titleId, int? issueId, CancellationToken ct)
    {
        var title = titles.Get(titleId) ?? throw new KeyNotFoundException("Title not found");
        var releases = await SearchIndexers(title, ct);
        Remember(releases);
        var result = decisions.Decide(releases, automatic: false, onlyTitle: title);
        if (issueId != null)
        {
            // Show the matching issue's releases first, then everything else for context.
            result = result.OrderByDescending(d => d.Issue?.Id == issueId).ToList();
        }

        return decisions.Prioritize(result).OrderByDescending(d => issueId == null || d.Issue?.Id == issueId).ToList();
    }

    public async Task<int> Automatic(int titleId, IReadOnlyCollection<int>? issueIds, CancellationToken ct)
    {
        var title = titles.Get(titleId) ?? throw new KeyNotFoundException("Title not found");
        var releases = await SearchIndexers(title, ct);
        var wanted = issueIds?.ToHashSet();
        var candidates = decisions.Decide(releases, automatic: true, onlyTitle: title)
            .Where(d => d.Approved && (wanted == null || wanted.Contains(d.Issue!.Id)));
        return await GrabBest(candidates, ct);
    }

    public async Task<int> MissingSearch(CancellationToken ct)
    {
        var today = clock.Today.ToString("yyyy-MM-dd");
        var grabbed = 0;
        foreach (var group in issues.Missing(today).GroupBy(i => i.TitleId))
        {
            try
            {
                grabbed += await Automatic(group.Key, group.Select(i => i.Id).ToList(), ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning("Search for title {TitleId} failed: {Message}", group.Key, e.Message);
            }
        }

        return grabbed;
    }

    public async Task<int> RssSync(CancellationToken ct)
    {
        var releases = new List<ReleaseInfo>();
        foreach (var (def, impl) in Enabled())
        {
            try
            {
                releases.AddRange(await impl.Recent(def, ct));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning("RSS sync from {Indexer} failed: {Message}", def.Name, e.Message);
            }
        }

        log.LogInformation("RSS sync: {Count} releases from indexers", releases.Count);
        return await GrabBest(decisions.Decide(releases, automatic: true).Where(d => d.Approved), ct);
    }

    public async Task<string> GrabRelease(string guid, int titleId, int? issueId, CancellationToken ct)
    {
        ReleaseInfo? release;
        lock (CacheLock)
        {
            release = RecentReleases.GetValueOrDefault(guid).Release;
        }

        if (release == null)
        {
            throw new KeyNotFoundException("That search result has expired. Search again.");
        }

        var title = titles.Get(titleId) ?? throw new KeyNotFoundException("Title not found");
        var decision = decisions.Decide([release], automatic: false, onlyTitle: title).Single();
        var issueList = issueId is { } id ? issues.Get([id]) : decision.Issue != null ? [decision.Issue] : [];
        return await downloads.Grab(release, title, issueList, ct);
    }

    private async Task<int> GrabBest(IEnumerable<DownloadDecision> approved, CancellationToken ct)
    {
        var grabbed = 0;
        foreach (var best in decisions.Prioritize(approved).GroupBy(d => d.Issue!.Id).Select(g => g.First()))
        {
            try
            {
                await downloads.Grab(best.Release, best.Title!, [best.Issue!], ct);
                grabbed++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning("Grabbing {Release} failed: {Message}", best.Release.Title, e.Message);
            }
        }

        return grabbed;
    }

    private async Task<List<ReleaseInfo>> SearchIndexers(Title title, CancellationToken ct)
    {
        var enabled = Enabled().ToList();
        if (enabled.Count == 0)
        {
            throw new InvalidOperationException("No indexers are enabled. Add one in Settings → Indexers.");
        }

        // Search by title name only and match issues locally: indexers' text search is too fuzzy
        // to rely on "Title 037" vs "Title #37" vs "Title November 2026".
        var tasks = enabled.Select(async x =>
        {
            try
            {
                return await x.Impl.Search(x.Def, title.Name, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning("Search on {Indexer} failed: {Message}", x.Def.Name, e.Message);
                return [];
            }
        });
        return (await Task.WhenAll(tasks)).SelectMany(r => r).ToList();
    }

    private IEnumerable<(ProviderDefinition Def, IIndexer Impl)> Enabled() =>
        indexerRepo.All()
            .Where(d => d.Enable)
            .Select(d => (Def: d, Impl: indexerImpls.FirstOrDefault(i => i.Implementation == d.Implementation)!))
            .Where(x => x.Impl != null);

    private static void Remember(IEnumerable<ReleaseInfo> releases)
    {
        lock (CacheLock)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-30);
            foreach (var k in RecentReleases.Where(kv => kv.Value.At < cutoff).Select(kv => kv.Key).ToList())
            {
                RecentReleases.Remove(k);
            }

            foreach (var r in releases)
            {
                RecentReleases[r.Guid] = (r, DateTime.UtcNow);
            }
        }
    }
}
