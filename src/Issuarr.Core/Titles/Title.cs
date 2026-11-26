namespace Issuarr.Core.Titles;

public enum TitleKind
{
    Comic = 0,
    Magazine = 1,
}

public enum TitleStatus
{
    Continuing = 0,
    Ended = 1,
}

/// <summary>A comic series or magazine that Issuarr tracks.</summary>
public class Title
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SortName { get; set; } = "";
    public string Slug { get; set; } = "";
    public TitleKind Kind { get; set; }
    public string? Publisher { get; set; }
    public int Year { get; set; }
    public string? Frequency { get; set; }
    public TitleStatus Status { get; set; }
    public bool Monitored { get; set; } = true;
    public string QualityProfile { get; set; } = "Any";
    public string Path { get; set; } = "";
    public string? Overview { get; set; }
    public List<string> Tags { get; set; } = [];

    /// <summary>Two colours used to paint a placeholder cover until real artwork exists.</summary>
    public List<string> Palette { get; set; } = [];

    public string Added { get; set; } = "";
}
