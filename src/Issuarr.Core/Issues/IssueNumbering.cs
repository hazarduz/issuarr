using System.Globalization;
using Issuarr.Core.Titles;

namespace Issuarr.Core.Issues;

public static class IssueNumbering
{
    /// <summary>How an issue is labelled: "#37" for comics, "Nov 2026", "2026-W48" or "Q3 2026" for magazines.</summary>
    public static string Format(TitleKind kind, string? frequency, int sequence, DateOnly releaseDate)
    {
        if (kind == TitleKind.Comic)
        {
            return $"#{sequence}";
        }

        return frequency?.ToLowerInvariant() switch
        {
            "weekly" => $"{releaseDate.Year}-W{ISOWeek.GetWeekOfYear(releaseDate.ToDateTime(TimeOnly.MinValue)):00}",
            "quarterly" => $"Q{(releaseDate.Month - 1) / 3 + 1} {releaseDate.Year}",
            _ => releaseDate.ToString("MMM yyyy", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>The numeric part of "#37" / "#12.5", or null for dated labels like "Nov 2026".</summary>
    public static decimal? GetNumber(string number) =>
        number.StartsWith('#') && decimal.TryParse(number[1..], NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;
}
