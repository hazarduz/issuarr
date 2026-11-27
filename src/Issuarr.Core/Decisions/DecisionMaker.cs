using Issuarr.Core.Blocklisting;
using Issuarr.Core.Configuration;
using Issuarr.Core.Indexers;
using Issuarr.Core.Issues;
using Issuarr.Core.Parser;
using Issuarr.Core.Titles;

namespace Issuarr.Core.Decisions;

public class DownloadDecision
{
    public ReleaseInfo Release { get; set; } = new();
    public ParsedRelease? Parsed { get; set; }
    public Title? Title { get; set; }
    public Issue? Issue { get; set; }
    public string? Format { get; set; }
    public List<string> Rejections { get; set; } = [];
    public bool Approved => Rejections.Count == 0;
}

public interface IDecisionMaker
{
    /// <param name="automatic">Automatic grabs also skip unmonitored issues; interactive search shows them with a reason.</param>
    List<DownloadDecision> Decide(IEnumerable<ReleaseInfo> releases, bool automatic, Title? onlyTitle = null);

    IEnumerable<DownloadDecision> Prioritize(IEnumerable<DownloadDecision> decisions);
}

public class DecisionMaker(
    ITitleService titles,
    IIssueRepository issues,
    IIssueFileRepository files,
    IBlocklistService blocklist,
    IConfigService config,
    IPendingDownloads pending) : IDecisionMaker
{
    public List<DownloadDecision> Decide(IEnumerable<ReleaseInfo> releases, bool automatic, Title? onlyTitle = null)
    {
        var allTitles = onlyTitle != null ? [onlyTitle] : titles.All();
        var issueCache = new Dictionary<int, List<Issue>>();
        var fileCache = new Dictionary<int, Dictionary<int, IssueFile>>();
        var pendingIssues = pending.PendingIssueIds();
        var decisions = new List<DownloadDecision>();

        List<Issue> IssuesOf(Title t)
        {
            if (!issueCache.TryGetValue(t.Id, out var list))
            {
                issueCache[t.Id] = list = issues.GetByTitle(t.Id);
                fileCache[t.Id] = files.GetByTitle(t.Id).ToDictionary(f => f.Id);
            }

            return list;
        }

        var authors = allTitles.Where(t => t.Kind == TitleKind.Book).ToList();
        var others = allTitles.Where(t => t.Kind != TitleKind.Book).ToList();

        foreach (var release in releases.DistinctBy(r => r.Guid))
        {
            var d = new DownloadDecision { Release = release, Parsed = ReleaseParser.Parse(release.Title) };
            decisions.Add(d);
            d.Format = d.Parsed?.Format ?? FormatFromTitle(release.Title);

            // Books first: their names don't follow the comic "Title 001 (Year)" pattern.
            if (authors.Count > 0 && BookMatcher.Match(release.Title, authors, IssuesOf) is { } book)
            {
                d.Title = book.Title;
                d.Issue = book.Book;
            }
            else if (onlyTitle?.Kind == TitleKind.Book)
            {
                d.Rejections.Add(BookMatcher.AuthorScore(BookMatcher.Normalize(release.Title), onlyTitle.Name) > 0
                    ? $"Couldn't match the release to one of {onlyTitle.Name}'s books"
                    : $"Release doesn't mention {onlyTitle.Name}");
                continue;
            }
            else
            {
                if (d.Parsed == null)
                {
                    d.Rejections.Add("Couldn't understand the release name");
                    continue;
                }

                var clean = ReleaseParser.CleanTitle(d.Parsed.TitleName);
                d.Title = others.FirstOrDefault(t => ReleaseParser.CleanTitle(t.Name) == clean)
                          ?? others.FirstOrDefault(t => ReleaseParser.CleanTitle($"{t.Name} {t.Year}") == clean);
                if (d.Title == null)
                {
                    d.Rejections.Add(onlyTitle != null ? $"Release is for \"{d.Parsed.TitleName}\", not {onlyTitle.Name}" : $"\"{d.Parsed.TitleName}\" isn't in your library");
                    continue;
                }

                d.Issue = MatchIssue(d.Parsed, IssuesOf(d.Title));
                if (d.Issue == null)
                {
                    d.Rejections.Add(d.Parsed.IssueNumber != null
                        ? $"Issue #{d.Parsed.IssueNumber} isn't known for {d.Title.Name}"
                        : d.Parsed.IsDated ? $"No {d.Title.Name} issue matches that date" : "No issue number or date in the release name");
                    continue;
                }
            }

            IssuesOf(d.Title);

            var profile = QualityProfiles.Get(d.Title.QualityProfile);
            if (!profile.Allows(d.Format))
            {
                d.Rejections.Add($"{d.Format} isn't allowed by the \"{profile.Name}\" profile");
            }

            if (d.Issue.IssueFileId is { } fileId && fileCache[d.Title.Id].TryGetValue(fileId, out var existing))
            {
                if (!profile.UpgradesAllowed)
                {
                    d.Rejections.Add("Already downloaded (profile doesn't allow upgrades)");
                }
                else if (profile.Rank(existing.Format) <= profile.Rank(d.Format))
                {
                    d.Rejections.Add($"Not an upgrade: you already have this issue as {existing.Format}");
                }
            }

            if (automatic && (!d.Title.Monitored || !d.Issue.Monitored))
            {
                d.Rejections.Add(d.Title.Monitored ? "Issue isn't monitored" : "Title isn't monitored");
            }

            if (automatic && pendingIssues.Contains(d.Issue.Id))
            {
                d.Rejections.Add("Already downloading");
            }

            if (release.Protocol == DownloadProtocol.Torrent && release.Seeders is { } seeders && seeders < config.MinimumSeeders)
            {
                d.Rejections.Add($"Not enough seeders ({seeders}, minimum {config.MinimumSeeders})");
            }

            if (blocklist.IsBlocklisted(d.Title.Id, release.Title))
            {
                d.Rejections.Add("Release is blocklisted");
            }
        }

        return decisions;
    }

    public IEnumerable<DownloadDecision> Prioritize(IEnumerable<DownloadDecision> decisions) =>
        decisions
            .OrderByDescending(d => d.Approved)
            .ThenBy(d => d.Title == null ? int.MaxValue : QualityProfiles.Get(d.Title.QualityProfile).Rank(d.Format))
            .ThenBy(d => d.Release.IndexerPriority)
            .ThenByDescending(d => d.Release.Protocol == DownloadProtocol.Torrent ? d.Release.Seeders ?? 0 : 1000)
            .ThenByDescending(d => d.Release.PublishDate ?? DateTime.MinValue);

    public static Issue? MatchIssue(ParsedRelease parsed, List<Issue> titleIssues)
    {
        if (parsed.IssueNumber is { } n)
        {
            return titleIssues.FirstOrDefault(i => IssueNumbering.GetNumber(i.Number) == n);
        }

        if (parsed.Year is not { } year)
        {
            return null;
        }

        if (parsed.Week is { } w)
        {
            return titleIssues.FirstOrDefault(i => i.Number == $"{year}-W{w:00}");
        }

        if (parsed.Quarter is { } q)
        {
            return titleIssues.FirstOrDefault(i => i.Number == $"Q{q} {year}");
        }

        if (parsed.Month is { } m)
        {
            var prefix = $"{year}-{m:00}";
            return titleIssues.FirstOrDefault(i => i.ReleaseDate.StartsWith(prefix, StringComparison.Ordinal));
        }

        return null;
    }

    private static string? FormatFromTitle(string title)
    {
        var t = title.ToUpperInvariant();
        foreach (var f in new[] { "CBZ", "CBR", "CB7", "PDF", "EPUB", "AZW3", "MOBI" })
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(t, $@"\b{f}\b"))
            {
                return f;
            }
        }

        return null;
    }
}

/// <summary>Issues that already have a download in progress (so RSS/search don't grab them twice).</summary>
public interface IPendingDownloads
{
    HashSet<int> PendingIssueIds();
}
