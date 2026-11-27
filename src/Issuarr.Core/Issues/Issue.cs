namespace Issuarr.Core.Issues;

public enum IssueStatus
{
    Downloaded,
    Missing,
    Downloading,
    Unaired,
    Unmonitored,
}

public class Issue
{
    public int Id { get; set; }
    public int TitleId { get; set; }

    /// <summary>Display number: "#37" for comics, "Nov 2026" or "2026-W48" for magazines.</summary>
    public string Number { get; set; } = "";

    /// <summary>Numeric key used for ordering (issue number, or yyyyMMdd for dated issues).</summary>
    public double SortKey { get; set; }

    public string? IssueTitle { get; set; }

    /// <summary>yyyy-MM-dd</summary>
    public string ReleaseDate { get; set; } = "";

    public bool Monitored { get; set; } = true;
    public int? IssueFileId { get; set; }
    public string? ExternalId { get; set; }
    public string? CoverUrl { get; set; }

    /// <summary>Deleted by the user; kept so a metadata refresh doesn't add it back.</summary>
    public bool Ignored { get; set; }
}

public class IssueFile
{
    public int Id { get; set; }
    public int TitleId { get; set; }
    public string RelativePath { get; set; } = "";
    public long Size { get; set; }
    public string Format { get; set; } = "";
    public string DateAdded { get; set; } = "";

    /// <summary>KOReader's document fingerprint (see KoSync.PartialMd5), filled in lazily.</summary>
    public string? PartialMd5 { get; set; }
}
