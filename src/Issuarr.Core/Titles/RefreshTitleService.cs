using System.Text.RegularExpressions;
using System.Globalization;
using Issuarr.Core.Issues;
using Issuarr.Core.Metadata;
using Microsoft.Extensions.Logging;

namespace Issuarr.Core.Titles;

public interface IRefreshTitleService
{
    /// <summary>Updates details and issue list from the metadata source (or the release schedule for magazines).</summary>
    Task<int> Refresh(int titleId, CancellationToken ct, DateOnly? firstIssueDate = null);

    Task RefreshAll(CancellationToken ct);
}

public class RefreshTitleService(
    ITitleRepository titleRepo,
    IIssueRepository issues,
    IEnumerable<IMetadataProvider> providers,
    IClock clock,
    ILogger<RefreshTitleService> log,
    MediaFiles.IDiskScanService? diskScan = null) : IRefreshTitleService
{
    public static readonly string[] Frequencies = ["Weekly", "Fortnightly", "Monthly", "Bi-monthly", "Quarterly", "Annually"];

    public async Task<int> Refresh(int titleId, CancellationToken ct, DateOnly? firstIssueDate = null)
    {
        var title = titleRepo.Get(titleId) ?? throw new KeyNotFoundException("Title not found");
        var provider = providers.FirstOrDefault(p => p.Source == title.MetadataSource);
        int added;
        if (provider != null && title.ExternalId != null)
        {
            added = await RefreshFromMetadata(title, provider, ct, firstIssueDate);
        }
        else if (title.Kind == TitleKind.Magazine && Step(title.Frequency) != null)
        {
            added = RefreshSchedule(title, firstIssueDate);
        }
        else
        {
            added = 0;
        }

        title.LastRefreshed = clock.UtcNow.ToString("O");
        titleRepo.Update(title);
        return added;
    }

    public async Task RefreshAll(CancellationToken ct)
    {
        foreach (var t in titleRepo.All())
        {
            try
            {
                diskScan?.Scan(t.Id);
                await Refresh(t.Id, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning("Refreshing {Title} failed: {Message}", t.Name, e.Message);
            }
        }
    }

    private async Task<int> RefreshFromMetadata(Title title, IMetadataProvider provider, CancellationToken ct, DateOnly? firstIssueDate = null)
    {
        var series = await provider.GetSeries(title.ExternalId!, ct);
        title.Overview = series.Overview ?? title.Overview;
        title.Publisher = series.Publisher ?? title.Publisher;
        title.CoverUrl = series.CoverUrl ?? title.CoverUrl;
        title.Year = series.Year ?? title.Year;

        var remote = await provider.GetIssues(title.ExternalId!, ct);
        var local = issues.GetByTitleIncludingIgnored(title.Id);
        var byExternal = local.Where(i => i.ExternalId != null).ToDictionary(i => i.ExternalId!);
        var newIssues = new List<Issue>();

        var isBook = title.Kind == TitleKind.Book;
        title.Frequency = series.Frequency ?? title.Frequency;
        foreach (var (info, index) in remote.Select((r, i) => (r, i)))
        {
            // Books are identified by their title; comics by issue number.
            var number = isBook ? (info.Name ?? "Untitled").Trim() : NormalizeNumber(info.Number);
            var date = MetadataDates.Pick(info) ?? $"{(title.Year > 0 ? title.Year : 1900)}-01-01";
            var sortKey = isBook ? DateSortKey(date, index) : (double)(IssueNumbering.GetNumber(number) ?? index);
            var existing = byExternal.GetValueOrDefault(info.ExternalId)
                           ?? local.FirstOrDefault(i => i.ExternalId == null && (isBook ? SameBook(i.Number, number) : i.Number == number));
            if (existing != null)
            {
                existing.Number = number;
                existing.SortKey = sortKey;
                existing.IssueTitle = isBook ? existing.IssueTitle : info.Name ?? existing.IssueTitle;
                if (!existing.ReleaseDateLocked)
                {
                    existing.ReleaseDate = date;
                }

                existing.ExternalId = info.ExternalId;
                existing.CoverUrl = info.CoverUrl ?? existing.CoverUrl;
                issues.Update(existing);
                continue;
            }

            newIssues.Add(new Issue
            {
                TitleId = title.Id,
                Number = number,
                SortKey = sortKey,
                IssueTitle = isBook ? null : info.Name,
                ReleaseDate = date,
                Monitored = title.Monitored,
                ExternalId = info.ExternalId,
                CoverUrl = info.CoverUrl,
            });
        }

        if (newIssues.Count > 0)
        {
            issues.InsertMany(newIssues);
        }

        // Magazine sources like Wikidata have details but no issue list: use the schedule.
        if (remote.Count == 0 && title.Kind == TitleKind.Magazine && Step(title.Frequency) != null)
        {
            return RefreshSchedule(title, firstIssueDate);
        }

        if (isBook)
        {
            return newIssues.Count; // authors don't "end" the way series do
        }

        if (title.Status == TitleStatus.Continuing && remote.Count > 0)
        {
            // Treat a series with nothing new for 18 months as ended.
            var latest = remote.Select(MetadataDates.Pick).Where(d => d != null).DefaultIfEmpty(null).Max();
            if (latest != null && DateOnly.Parse(latest, CultureInfo.InvariantCulture) < clock.Today.AddMonths(-18))
            {
                title.Status = TitleStatus.Ended;
            }
        }

        return newIssues.Count;
    }

    /// <summary>
    /// Magazines rarely have a metadata source, so their issues are generated from the
    /// release frequency: every expected issue up to today, plus the next one.
    /// </summary>
    private int RefreshSchedule(Title title, DateOnly? firstIssueDate)
    {
        var step = Step(title.Frequency)!;
        var local = issues.GetByTitleIncludingIgnored(title.Id);
        var known = local.Select(i => i.Number).ToHashSet();
        var last = local.Select(i => DateOnly.Parse(i.ReleaseDate, CultureInfo.InvariantCulture)).DefaultIfEmpty().Max();

        var horizon = clock.Today;

        // Magazines named by issue number (e.g. "0258 - Retro Gamer.pdf") continue their numbering.
        // Their dates are usually guesses, so rather than back-filling by date, only the next issue
        // is added, and the one after that once its expected date has passed.
        var visible = local.Where(i => !i.Ignored).ToList();
        if (visible.Count > 0 && visible.Count(i => IssueNumbering.GetNumber(i.Number) != null) * 2 > visible.Count)
        {
            var numberedIssues = visible.Where(i => IssueNumbering.GetNumber(i.Number) != null).ToList();
            var latest = numberedIssues.MaxBy(i => IssueNumbering.GetNumber(i.Number))!;
            var latestDate = DateOnly.Parse(latest.ReleaseDate, CultureInfo.InvariantCulture);
            if (latest.IssueFileId == null && latestDate > horizon)
            {
                return 0; // the next issue is already waiting
            }

            var nextNumber = Math.Floor(IssueNumbering.GetNumber(latest.Number)!.Value) + 1;
            var label = "#" + nextNumber.ToString("0", CultureInfo.InvariantCulture);
            if (known.Contains(label))
            {
                return 0;
            }

            var due = step(latestDate > horizon ? latestDate : horizon);
            issues.InsertMany([new Issue { TitleId = title.Id, Number = label, SortKey = (double)nextNumber, ReleaseDate = due.ToString("yyyy-MM-dd"), Monitored = title.Monitored }]);
            return 1;
        }

        if (firstIssueDate == null && local.Count > 0 && last > horizon)
        {
            return 0; // The next issue is already scheduled.
        }

        var next = firstIssueDate ?? (local.Count == 0 ? clock.Today : step(last));
        var created = new List<Issue>();
        var guard = 0;
        while (guard++ < 600)
        {
            var number = IssueNumbering.Format(TitleKind.Magazine, title.Frequency, 0, next);
            if (!known.Contains(number))
            {
                created.Add(new Issue
                {
                    TitleId = title.Id,
                    Number = number,
                    SortKey = next.Year * 10000 + next.Month * 100 + next.Day,
                    ReleaseDate = next.ToString("yyyy-MM-dd"),
                    Monitored = title.Monitored,
                });
                known.Add(number);
            }

            if (next > horizon)
            {
                break; // One upcoming issue is enough.
            }

            next = step(next);
        }

        if (created.Count > 0)
        {
            issues.InsertMany(created);
        }

        return created.Count;
    }

    public static Func<DateOnly, DateOnly>? Step(string? frequency) => frequency?.ToLowerInvariant() switch
    {
        "weekly" => d => d.AddDays(7),
        "fortnightly" => d => d.AddDays(14),
        "monthly" => d => d.AddMonths(1),
        "bi-monthly" or "bimonthly" => d => d.AddMonths(2),
        "quarterly" => d => d.AddMonths(3),
        "annually" or "yearly" => d => d.AddYears(1),
        _ => null,
    };

    private static double DateSortKey(string date, int index) =>
        DateOnly.TryParse(date, CultureInfo.InvariantCulture, out var d) ? d.Year * 10000 + d.Month * 100 + d.Day + index / 1000.0 : index;

    private static bool SameBook(string a, string b) =>
        string.Equals(Regex.Replace(a.ToLowerInvariant(), "[^a-z0-9]", ""), Regex.Replace(b.ToLowerInvariant(), "[^a-z0-9]", ""), StringComparison.Ordinal);

    public static string NormalizeNumber(string raw)
    {
        var trimmed = raw.Trim().TrimStart('#');
        return decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
            ? "#" + n.ToString("0.##", CultureInfo.InvariantCulture)
            : "#" + trimmed;
    }
}
