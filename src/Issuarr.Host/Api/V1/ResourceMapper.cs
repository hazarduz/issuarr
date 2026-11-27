using Issuarr.Core;
using Issuarr.Core.Issues;
using Issuarr.Core.Titles;

namespace Issuarr.Host.Api.V1;

/// <summary>Builds API resources from models, including derived status and statistics.</summary>
public class ResourceMapper(IIssueRepository issues, IIssueFileRepository files, IClock clock, Issuarr.Core.Decisions.IPendingDownloads pending, Issuarr.Core.Reading.IReadProgressRepository progress)
{
    public List<TitleResource> Titles(IEnumerable<Title> titles, bool includeIssues)
    {
        var allIssues = issues.All().ToLookup(i => i.TitleId);
        var allFiles = files.All().ToDictionary(f => f.Id);
        return titles.Select(t => Title(t, allIssues[t.Id].OrderByDescending(i => i.SortKey).ToList(), allFiles, includeIssues)).ToList();
    }

    public TitleResource Title(Title title, bool includeIssues)
    {
        var fileMap = files.GetByTitle(title.Id).ToDictionary(f => f.Id);
        return Title(title, issues.GetByTitle(title.Id), fileMap, includeIssues);
    }

    public IssueResource Issue(Issue issue, Title title, IReadOnlyDictionary<int, IssueFile> fileMap, IReadOnlyDictionary<int, double>? readPercent = null)
    {
        IssueFileResource? file = null;
        if (issue.IssueFileId is { } fid && fileMap.TryGetValue(fid, out var f))
        {
            file = new IssueFileResource(f.Id, Path.GetFileName(f.RelativePath), f.Size, f.Format);
        }

        return new IssueResource(
            issue.Id,
            issue.TitleId,
            issue.Number,
            issue.IssueTitle,
            issue.ReleaseDate,
            issue.Monitored,
            IssueStatusCalculator.Calculate(issue, title.Monitored, clock.Today, pending.PendingIssueIds().Contains(issue.Id)),
            file,
            issue.CoverUrl,
            readPercent?.GetValueOrDefault(issue.Id) is var p and > 0 ? p : null,
            issue.ReleaseDateLocked);
    }

    private TitleResource Title(Title title, List<Issue> titleIssues, IReadOnlyDictionary<int, IssueFile> fileMap, bool includeIssues)
    {
        var read = includeIssues ? progress.PercentByIssue(titleIssues.Where(i => i.IssueFileId != null).Select(i => i.Id)) : null;
        var mapped = titleIssues.Select(i => Issue(i, title, fileMap, read)).ToList();
        var aired = mapped.Where(i => i.Status != IssueStatus.Unaired).ToList();
        // Like Sonarr: the total is aired issues you want (monitored) plus any you already have.
        var wanted = aired.Count(i => i.Monitored || i.File != null);
        var next = mapped.Where(i => i.Status == IssueStatus.Unaired).MinBy(i => i.ReleaseDate);

        return new TitleResource
        {
            Id = title.Id,
            Slug = title.Slug,
            Name = title.Name,
            SortName = title.SortName,
            Kind = title.Kind,
            Publisher = title.Publisher,
            Year = title.Year,
            Frequency = title.Frequency,
            Status = title.Status,
            Monitored = title.Monitored,
            QualityProfile = title.QualityProfile,
            Path = title.Path,
            Overview = title.Overview,
            Tags = title.Tags,
            Palette = title.Palette,
            Added = title.Added,
            MetadataSourceName = title.MetadataSource,
            ExternalId = title.ExternalId,
            // A custom cover is served by Issuarr; the version in the name busts browser caches.
            Cover = title.CustomCover != null ? $"/api/v1/mediacover/{title.Id}?v={Path.GetFileNameWithoutExtension(title.CustomCover)}" : title.CoverUrl,
            LastRefreshed = title.LastRefreshed,
            Statistics = new TitleStatistics(
                IssueCount: wanted > 0 ? wanted : aired.Count,
                IssueFileCount: aired.Count(i => i.File != null),
                MissingCount: aired.Count(i => i.Status == IssueStatus.Missing),
                SizeOnDisk: mapped.Sum(i => i.File?.Size ?? 0),
                NextIssue: next?.ReleaseDate,
                HiddenCount: issues.CountIgnored(title.Id)),
            Issues = includeIssues ? mapped : null,
        };
    }
}
