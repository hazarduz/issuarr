using Issuarr.Core.Issues;
using Issuarr.Core.Titles;

namespace Issuarr.Core.Reading;

public record ShelfItem(
    int IssueId, int TitleId, string TitleName, string TitleSlug, TitleKind Kind, string Number, string? IssueTitle,
    string Format, double? Percent, string? When);

public record ReadingHome(
    List<ShelfItem> ContinueReading, List<ShelfItem> UpNext, List<ShelfItem> RecentlyAdded,
    List<ShelfItem> LatestComics, List<ShelfItem> LatestMagazines, List<ShelfItem> LatestBooks);

public interface IReadingHomeService
{
    ReadingHome Get(int userId, int limit = 20);
}

/// <summary>Builds the Home page shelves (Plex's "On Deck"): what you're reading, what's next, what's new.</summary>
public class ReadingHomeService(IReadProgressRepository progress, IIssueRepository issues, IIssueFileRepository files, ITitleService titles) : IReadingHomeService
{
    public const double Finished = 98;

    public ReadingHome Get(int userId, int limit = 20)
    {
        var titleMap = titles.All().ToDictionary(t => t.Id);
        var allIssues = issues.All(); // excludes hidden
        var issueMap = allIssues.ToDictionary(i => i.Id);
        var fileMap = files.All().ToDictionary(f => f.Id);
        var saved = progress.All(userId); // newest first

        ShelfItem? Item(int issueId, double? percent, string? when)
        {
            if (!issueMap.TryGetValue(issueId, out var i) || i.IssueFileId is not { } fid || !fileMap.TryGetValue(fid, out var f) || !titleMap.TryGetValue(i.TitleId, out var t))
            {
                return null;
            }

            return new ShelfItem(i.Id, t.Id, t.Name, t.Slug, t.Kind, i.Number, i.IssueTitle, f.Format, percent, when);
        }

        var reading = saved.Where(p => p.Percent > 0.5 && p.Percent < Finished)
            .Select(p => Item(p.IssueId, p.Percent, p.Updated)).OfType<ShelfItem>().Take(limit).ToList();

        // For each title with a finished issue (and nothing in progress), the next issue that has a file.
        var readingTitles = reading.Select(r => r.TitleId).ToHashSet();
        var percentByIssue = saved.ToDictionary(p => p.IssueId, p => p.Percent);
        var upNext = new List<ShelfItem>();
        foreach (var finished in saved.Where(p => p.Percent >= Finished))
        {
            if (!issueMap.TryGetValue(finished.IssueId, out var done) || readingTitles.Contains(done.TitleId) || upNext.Any(u => u.TitleId == done.TitleId))
            {
                continue;
            }

            var next = allIssues.Where(i => i.TitleId == done.TitleId && i.SortKey > done.SortKey && i.IssueFileId != null)
                .OrderBy(i => i.SortKey)
                .FirstOrDefault(i => percentByIssue.GetValueOrDefault(i.Id) < Finished);
            if (next != null && Item(next.Id, percentByIssue.TryGetValue(next.Id, out var p) ? p : null, finished.Updated) is { } item)
            {
                upNext.Add(item);
            }

            if (upNext.Count >= limit)
            {
                break;
            }
        }

        // Newest files first; files imported together (e.g. a whole back catalogue) newest issue first.
        var issueByFile = allIssues.Where(i => i.IssueFileId != null).GroupBy(i => i.IssueFileId!.Value).ToDictionary(g => g.Key, g => g.First());
        var added = fileMap.Values
            .Where(f => issueByFile.ContainsKey(f.Id))
            .Select(f => (File: f, Issue: issueByFile[f.Id]))
            .OrderByDescending(x => x.File.DateAdded)
            .ThenByDescending(x => x.Issue.ReleaseDate)
            .ThenByDescending(x => x.Issue.SortKey)
            .Select(x => Item(x.Issue.Id, percentByIssue.TryGetValue(x.Issue.Id, out var p) ? p : null, x.File.DateAdded))
            .OfType<ShelfItem>()
            .ToList();

        List<ShelfItem> Latest(TitleKind kind) => added.Where(a => a.Kind == kind).Take(limit).ToList();
        return new ReadingHome(reading, upNext, added.Take(limit).ToList(), Latest(TitleKind.Comic), Latest(TitleKind.Magazine), Latest(TitleKind.Book));
    }
}
