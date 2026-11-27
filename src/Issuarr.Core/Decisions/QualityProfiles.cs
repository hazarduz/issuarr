namespace Issuarr.Core.Decisions;

/// <summary>
/// Which file formats a title accepts, best first. Releases in a better format
/// replace existing files until the cutoff format is reached.
/// </summary>
public record QualityProfile(string Name, string[] Formats, bool UpgradesAllowed, string Description)
{
    public string Cutoff => Formats[0];

    /// <summary>Lower is better. Unknown formats (the release name didn't say) rank last but are allowed.</summary>
    public int Rank(string? format) =>
        format == null ? Formats.Length : Array.IndexOf(Formats, format.ToUpperInvariant()) is var i and >= 0 ? i : int.MaxValue;

    public bool Allows(string? format) => Rank(format) != int.MaxValue;
}

public static class QualityProfiles
{
    public static readonly QualityProfile[] All =
    [
        new("CBZ / CBR", ["CBZ", "CBR", "CB7"], true, "Comic archives only. CBR and CB7 are upgraded to CBZ."),
        new("PDF Preferred", ["PDF", "EPUB", "CBZ", "CBR"], true, "Best for magazines. Anything else is upgraded to PDF."),
        new("Any", ["CBZ", "CBR", "CB7", "PDF", "EPUB"], false, "Any format, first one wins, no upgrades."),
    ];

    public static QualityProfile Get(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? All[^1];
}
