using Issuarr.Core;
using Issuarr.Core.Issues;
using Issuarr.Core.Titles;

namespace Issuarr.Host.Api.V1;

/// <summary>Builds API resources from models, including derived status and statistics.</summary>
public class ResourceMapper(IIssueRepository issues, IIssueFileRepository files, IClock clock, Issuarr.Core.Decisions.IPendingDownloads pending)
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

    public IssueResource Issue(Issue issue, Title title, IReadOnlyDictionary<int, IssueFile> fileMap)
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
            issue.CoverUrl);
    }

    private TitleResource Title(Title title, List<Issue> titleIssues, IReadOnlyDictionary<int, IssueFile> fileMap, bool includeIssues)
    {
        var mapped = titleIssues.Select(i => Issue(i, title, fileMap)).ToList();
        var aired = mapped.Where(i => i.Status != IssueStatus.Unaired).ToList();
        var monitoredAired = aired.Count(i => i.Monitored);
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
            Cover = title.CoverUrl,
            LastRefreshed = title.LastRefreshed,
            Statistics = new TitleStatistics(
                IssueCount: monitoredAired > 0 ? monitoredAired : aired.Count,
                IssueFileCount: aired.Count(i => i.File != null),
                MissingCount: aired.Count(i => i.Status == IssueStatus.Missing),
                SizeOnDisk: mapped.Sum(i => i.File?.Size ?? 0),
                NextIssue: next?.ReleaseDate,
                HiddenCount: issues.CountIgnored(title.Id)),
            Issues = includeIssues ? mapped : null,
        };
    }
}
