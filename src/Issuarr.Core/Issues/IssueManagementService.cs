using System.Globalization;
using Issuarr.Core.Metadata;
using Issuarr.Core.Parser;
using Issuarr.Core.Titles;

namespace Issuarr.Core.Issues;

/// <summary>An entry from the title's metadata source that an issue/book can be matched to.</summary>
public record MetadataCandidate(
    string ExternalId, string Number, string? Name, string? ReleaseDate, string? CoverUrl,
    int? LocalIssueId, bool LocalHasFile, bool LocalHidden);

public interface IIssueManagementService
{
    Task<List<MetadataCandidate>> Candidates(int titleId, CancellationToken ct);

    /// <summary>
    /// Points an issue/book at a metadata entry. If another row already represents that entry
    /// (e.g. the imported "Project Hail Mary - Andy Weir" vs Open Library's "Project Hail Mary"),
    /// the two are merged: the file moves onto that row and the duplicate is removed.
    /// </summary>
    Task<Issue> Match(int issueId, string externalId, CancellationToken ct);

    Issue Edit(int issueId, string number, string? issueTitle, string releaseDate);

    /// <summary>Removes an issue/book. Metadata-backed ones are hidden so a refresh doesn't add them back.</summary>
    void Delete(int issueId, bool deleteFile);

    void RestoreHidden(int titleId);
}

public class IssueManagementService(
    ITitleService titles,
    IIssueRepository issues,
    IIssueFileRepository files,
    ITitleManagementService management,
    IEnumerable<IMetadataProvider> providers) : IIssueManagementService
{
    public async Task<List<MetadataCandidate>> Candidates(int titleId, CancellationToken ct)
    {
        var (title, remote) = await Remote(titleId, ct);
        var local = issues.GetByTitleIncludingIgnored(titleId);
        var byExternal = local.Where(i => i.ExternalId != null).GroupBy(i => i.ExternalId!).ToDictionary(g => g.Key, g => g.First());
        return remote.Select(info =>
        {
            var (number, name, date) = Describe(title, info);
            var mine = byExternal.GetValueOrDefault(info.ExternalId);
            return new MetadataCandidate(info.ExternalId, number, name, date, info.CoverUrl, mine?.Id, mine?.IssueFileId != null, mine?.Ignored == true);
        }).ToList();
    }

    public async Task<Issue> Match(int issueId, string externalId, CancellationToken ct)
    {
        var issue = issues.Get(issueId) ?? throw new KeyNotFoundException("Not found");
        var (title, remote) = await Remote(issue.TitleId, ct);
        var info = remote.FirstOrDefault(r => r.ExternalId == externalId) ?? throw new ArgumentException("That entry isn't in the title's metadata any more. Refresh the title and try again.");
        var (number, name, date) = Describe(title, info);

        var target = issues.GetByTitleIncludingIgnored(title.Id).FirstOrDefault(i => i.Id != issue.Id && i.ExternalId == externalId);
        if (target != null)
        {
            if (issue.IssueFileId != null && target.IssueFileId != null)
            {
                throw new ArgumentException($"\"{target.Number}\" already has a file. Delete one of the two files first.");
            }

            target.IssueFileId ??= issue.IssueFileId;
            target.Monitored = target.Monitored || issue.Monitored;
            target.Ignored = false;
            Apply(title, target, number, name, date, info.CoverUrl);
            issues.Update(target);
            issues.Delete([issue.Id]);
            return target;
        }

        issue.ExternalId = externalId;
        issue.Ignored = false;
        Apply(title, issue, number, name, date, info.CoverUrl);
        issues.Update(issue);
        return issue;
    }

    public Issue Edit(int issueId, string number, string? issueTitle, string releaseDate)
    {
        var issue = issues.Get(issueId) ?? throw new KeyNotFoundException("Not found");
        var title = titles.Get(issue.TitleId)!;
        number = number.Trim();
        if (title.Kind == TitleKind.Comic && !number.StartsWith('#') && decimal.TryParse(number, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            number = "#" + number;
        }

        if (number.Length == 0)
        {
            throw new ArgumentException(title.Kind == TitleKind.Book ? "The book needs a title" : "The issue needs a number");
        }

        if (!DateOnly.TryParse(releaseDate, CultureInfo.InvariantCulture, out var date))
        {
            throw new ArgumentException("Release date must be a date (yyyy-MM-dd)");
        }

        if (issues.GetByTitle(title.Id).Any(i => i.Id != issueId && SameNumber(title, i.Number, number)))
        {
            throw new ArgumentException($"{title.Name} already has \"{number}\". Use Match to merge them instead.");
        }

        Apply(title, issue, number, title.Kind == TitleKind.Book ? null : (string.IsNullOrWhiteSpace(issueTitle) ? null : issueTitle.Trim()), date.ToString("yyyy-MM-dd"), issue.CoverUrl);
        issues.Update(issue);
        return issue;
    }

    public void Delete(int issueId, bool deleteFile)
    {
        var issue = issues.Get(issueId) ?? throw new KeyNotFoundException("Not found");
        if (issue.IssueFileId is { } fileId)
        {
            if (deleteFile)
            {
                management.DeleteIssueFile(fileId);
            }
            else
            {
                // Keep the file on disk; Issuarr just stops tracking it.
                issues.ClearFile(fileId);
                files.Delete(fileId);
            }
        }

        if (issue.ExternalId != null)
        {
            var fresh = issues.Get(issueId)!;
            fresh.Ignored = true;
            fresh.Monitored = false;
            issues.Update(fresh);
        }
        else
        {
            issues.Delete([issueId]);
        }
    }

    public void RestoreHidden(int titleId) => issues.RestoreIgnored(titleId);

    private async Task<(Title Title, List<IssueInfo> Remote)> Remote(int titleId, CancellationToken ct)
    {
        var title = titles.Get(titleId) ?? throw new KeyNotFoundException("Title not found");
        var provider = providers.FirstOrDefault(p => p.Source == title.MetadataSource);
        if (provider == null || title.ExternalId == null)
        {
            throw new ArgumentException($"{title.Name} isn't matched to a metadata source yet. Match the title first (toolbar → Match), or use Edit.");
        }

        var remote = await provider.GetIssues(title.ExternalId, ct);
        if (remote.Count == 0)
        {
            throw new ArgumentException($"{provider.DisplayName} has no {(title.Kind == TitleKind.Book ? "book" : "issue")} list for {title.Name}. Use Edit instead.");
        }

        return (title, remote);
    }

    private static (string Number, string? Name, string Date) Describe(Title title, IssueInfo info)
    {
        var isBook = title.Kind == TitleKind.Book;
        var number = isBook ? (info.Name ?? "Untitled").Trim() : RefreshTitleService.NormalizeNumber(info.Number);
        var date = MetadataDates.Pick(info) ?? $"{(title.Year > 0 ? title.Year : 1900)}-01-01";
        return (number, isBook ? null : info.Name, date);
    }

    private static void Apply(Title title, Issue issue, string number, string? name, string date, string? cover)
    {
        issue.Number = number;
        issue.IssueTitle = name;
        issue.ReleaseDate = date;
        issue.CoverUrl = cover ?? issue.CoverUrl;
        var d = DateOnly.Parse(date, CultureInfo.InvariantCulture);
        issue.SortKey = title.Kind == TitleKind.Comic && IssueNumbering.GetNumber(number) is { } n
            ? (double)n
            : d.Year * 10000 + d.Month * 100 + d.Day;
    }

    private static bool SameNumber(Title title, string a, string b) =>
        title.Kind == TitleKind.Book ? BookMatcher.Normalize(a) == BookMatcher.Normalize(b) : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
