using Issuarr.Core.Issues;
using Issuarr.Core.Titles;

namespace Issuarr.Host.Api.V1;

public record IssueFileResource(int Id, string Name, long Size, string Format);

public record IssueResource(
    int Id,
    int TitleId,
    string Number,
    string? Title,
    string ReleaseDate,
    bool Monitored,
    IssueStatus Status,
    IssueFileResource? File,
    string? CoverUrl = null,
    double? ReadPercent = null,
    bool ReleaseDateLocked = false);

public record TitleStatistics(int IssueCount, int IssueFileCount, int MissingCount, long SizeOnDisk, string? NextIssue, int HiddenCount = 0);

public record TitleResource
{
    public int Id { get; init; }
    public string Slug { get; init; } = "";
    public string Name { get; init; } = "";
    public string SortName { get; init; } = "";
    public TitleKind Kind { get; init; }
    public string? Publisher { get; init; }
    public int Year { get; init; }
    public string? Frequency { get; init; }
    public TitleStatus Status { get; init; }
    public bool Monitored { get; init; } = true;
    public string QualityProfile { get; init; } = "";
    public string Path { get; init; } = "";
    public string? Overview { get; init; }
    public List<string> Tags { get; init; } = [];
    public List<string> Palette { get; init; } = [];
    public string Added { get; init; } = "";
    public string? MetadataSourceName { get; init; }
    public string? ExternalId { get; init; }
    public string? Cover { get; init; }
    public string? LastRefreshed { get; init; }
    public TitleStatistics? Statistics { get; init; }
    public List<IssueResource>? Issues { get; init; }

    public Title ToModel() => new()
    {
        Id = Id,
        Name = Name,
        Kind = Kind,
        Publisher = Publisher,
        Year = Year,
        Frequency = Frequency,
        Status = Status,
        Monitored = Monitored,
        QualityProfile = string.IsNullOrWhiteSpace(QualityProfile) ? "Any" : QualityProfile,
        Path = Path,
        Overview = Overview,
        Tags = Tags,
        Palette = Palette,
    };
}

public record AddTitleResource : TitleResource
{
    public string? RootFolderPath { get; init; }
    public string? MetadataSource { get; init; }
    public string? CoverUrl { get; init; }

    /// <summary>all | future | missing | none</summary>
    public string Monitor { get; init; } = "all";

    public bool SearchForMissing { get; init; }

    /// <summary>For magazines without a metadata source: date of the first issue to track (yyyy-MM-dd).</summary>
    public string? FirstIssueDate { get; init; }

    /// <summary>eBooks: add the author but only monitor this book.</summary>
    public string? BookExternalId { get; init; }

    public string? BookTitle { get; init; }
}

public record MonitorIssuesResource(List<int> IssueIds, bool Monitored);
