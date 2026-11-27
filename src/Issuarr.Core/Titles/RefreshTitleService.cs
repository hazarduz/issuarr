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
    ILogger<RefreshTitleService> log) : IRefreshTitleService
{
    public static readonly string[] Frequencies = ["Weekly", "Fortnightly", "Monthly", "Bi-monthly", "Quarterly", "Annually"];

    public async Task<int> Refresh(int titleId, CancellationToken ct, DateOnly? firstIssueDate = null)
    {
        var title = titleRepo.Get(titleId) ?? throw new KeyNotFoundException("Title not found");
        var provider = providers.FirstOrDefault(p => p.Source == title.MetadataSource);
        int added;
        if (provider != null && title.ExternalId != null)
        {
            added = await RefreshFromMetadata(title, provider, ct);
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
                await Refresh(t.Id, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning("Refreshing {Title} failed: {Message}", t.Name, e.Message);
            }
        }
    }

    private async Task<int> RefreshFromMetadata(Title title, IMetadataProvider provider, CancellationToken ct)
    {
        var series = await provider.GetSeries(title.ExternalId!, ct);
        title.Overview = series.Overview ?? title.Overview;
        title.Publisher = series.Publisher ?? title.Publisher;
        title.CoverUrl = series.CoverUrl ?? title.CoverUrl;
        title.Year = series.Year ?? title.Year;

        var remote = await provider.GetIssues(title.ExternalId!, ct);
        var local = issues.GetByTitle(title.Id);
        var byExternal = local.Where(i => i.ExternalId != null).ToDictionary(i => i.ExternalId!);
        var newIssues = new List<Issue>();

        foreach (var (info, index) in remote.Select((r, i) => (r, i)))
        {
            var number = NormalizeNumber(info.Number);
            var date = MetadataDates.Pick(info) ?? $"{title.Year}-01-01";
            var existing = byExternal.GetValueOrDefault(info.ExternalId) ?? local.FirstOrDefault(i => i.ExternalId == null && i.Number == number);
            if (existing != null)
            {
                existing.Number = number;
                existing.SortKey = (double)(IssueNumbering.GetNumber(number) ?? index);
                existing.IssueTitle = info.Name ?? existing.IssueTitle;
                existing.ReleaseDate = date;
                existing.ExternalId = info.ExternalId;
                existing.CoverUrl = info.CoverUrl ?? existing.CoverUrl;
                issues.Update(existing);
                continue;
            }

            newIssues.Add(new Issue
            {
                TitleId = title.Id,
                Number = number,
                SortKey = (double)(IssueNumbering.GetNumber(number) ?? index),
                IssueTitle = info.Name,
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
        var local = issues.GetByTitle(title.Id);
        var known = local.Select(i => i.Number).ToHashSet();
        var last = local.Select(i => DateOnly.Parse(i.ReleaseDate, CultureInfo.InvariantCulture)).DefaultIfEmpty().Max();

        var horizon = clock.Today;
        if (local.Count > 0 && last > horizon)
        {
            return 0; // The next issue is already scheduled.
        }

        var next = local.Count == 0 ? firstIssueDate ?? clock.Today : step(last);
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

    public static string NormalizeNumber(string raw)
    {
        var trimmed = raw.Trim().TrimStart('#');
        return decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
            ? "#" + n.ToString("0.##", CultureInfo.InvariantCulture)
            : "#" + trimmed;
    }
}
