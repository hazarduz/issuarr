using System.Globalization;
using System.Text.RegularExpressions;

namespace Issuarr.Core.Parser;

public class ParsedRelease
{
    public string TitleName { get; set; } = "";

    /// <summary>Comic issue number, e.g. 37 or 12.5. Null for dated (magazine-style) releases.</summary>
    public decimal? IssueNumber { get; set; }

    public int? Year { get; set; }
    public int? Month { get; set; }
    public int? Week { get; set; }
    public int? Quarter { get; set; }

    /// <summary>CBZ, CBR, PDF, EPUB or null when the name does not say.</summary>
    public string? Format { get; set; }

    public bool IsDigital { get; set; }
    public bool IsTruePdf { get; set; }
    public string? ReleaseGroup { get; set; }

    public bool IsDated => Month.HasValue || Week.HasValue || Quarter.HasValue;
}

/// <summary>
/// Turns release names from indexers into structured data. Handles the
/// common shapes for both comics and magazines, for example:
/// <list type="bullet">
/// <item>Starfall.Rangers.037.2026.Digital.cbz</item>
/// <item>Iron Tide #46 (2026) (Webrip) (Zone-Empire).cbr</item>
/// <item>Circuit.and.Solder.November.2026.PDF</item>
/// <item>Trail.Weekly.2026.W48.True.PDF</item>
/// <item>Velocity - 2026-11.pdf</item>
/// <item>Pixel Quarterly Q3 2026</item>
/// </list>
/// </summary>
public static partial class ReleaseParser
{
    private static readonly string[] MonthNames = CultureInfo.InvariantCulture.DateTimeFormat.MonthNames
        .Where(m => m.Length > 0).ToArray();

    private static readonly string[] MonthAbbrevs = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames
        .Where(m => m.Length > 0).ToArray();

    [GeneratedRegex(@"\.(?<ext>cbz|cbr|cb7|pdf|epub|mobi|azw3|azw)$", RegexOptions.IgnoreCase)]
    private static partial Regex ExtensionRegex();

    [GeneratedRegex(@"\b(?<fmt>cbz|cbr|cb7|pdf|epub|mobi|azw3)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FormatTokenRegex();

    // "(Zone-Empire)" at the end, or "-GROUP" at the end of a scene-style name.
    [GeneratedRegex(@"(?:\((?<group>[^()]*-[^()]*|[A-Za-z0-9]+-[A-Za-z0-9]+)\)\s*$)|(?:-(?<group>[A-Za-z0-9]*[A-Za-z][A-Za-z0-9]*)$)")]
    private static partial Regex GroupRegex();

    // Year + ISO week: 2026.W48 / 2026-W48 / W48 2026
    [GeneratedRegex(@"\b(?<year>(?:19|20)\d{2})[\s.\-]?W(?<week>\d{1,2})\b|\bW(?<week>\d{1,2})[\s.\-]+(?<year>(?:19|20)\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex WeekRegex();

    // Quarter: Q3 2026 / 2026 Q3
    [GeneratedRegex(@"\bQ(?<q>[1-4])[\s.\-]+(?<year>(?:19|20)\d{2})\b|\b(?<year>(?:19|20)\d{2})[\s.\-]+Q(?<q>[1-4])\b", RegexOptions.IgnoreCase)]
    private static partial Regex QuarterRegex();

    // Numeric date: 2026-11 / 2026.11 / 2026-11-07
    [GeneratedRegex(@"\b(?<year>(?:19|20)\d{2})[.\-](?<month>0[1-9]|1[0-2])(?:[.\-](?<day>\d{2}))?\b")]
    private static partial Regex NumericDateRegex();

    // Issue number: #46, 046, 12.5, "Issue 112", "No. 7"
    [GeneratedRegex(@"(?:#|\b(?:issue|no\.?)\s*)(?<num>\d{1,4}(?:\.\d)?)\b|(?<![\d.])\b(?<num>\d{1,4}(?:\.5)?)\b(?![.\-]\d)", RegexOptions.IgnoreCase)]
    private static partial Regex IssueRegex();

    [GeneratedRegex(@"\(?\b(?<year>(?:19|20)\d{2})\b\)?")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"\b(digital|webrip|web|true|scan|c2c|hd|retail|repack|proper|ebook|magazine|magazines)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseRegex();

    [GeneratedRegex(@"[\s]{2,}")]
    private static partial Regex MultiSpace();

    public static ParsedRelease? Parse(string releaseName)
    {
        if (string.IsNullOrWhiteSpace(releaseName))
        {
            return null;
        }

        var result = new ParsedRelease();
        var name = releaseName.Trim();

        var ext = ExtensionRegex().Match(name);
        if (ext.Success)
        {
            result.Format = ext.Groups["ext"].Value.ToUpperInvariant();
            name = name[..ext.Index];
        }

        var group = GroupRegex().Match(name);
        if (group.Success)
        {
            result.ReleaseGroup = group.Groups["group"].Value;
            name = name[..group.Index];
        }

        // Normalise separators so "Starfall.Rangers.037" and "Starfall_Rangers_037" look like "Starfall Rangers 037".
        // Decimal issue numbers (12.5) keep their dot; hyphens are left alone for dates (2026-11).
        name = Regex.Replace(name, @"(\d)\.(\d)(?!\d)", "$1\u0001$2");
        name = name.Replace('.', ' ').Replace('_', ' ').Replace('\u0001', '.');
        name = name.Replace("(", " ( ").Replace(")", " ) ").Replace("[", " ( ").Replace("]", " ) ");

        result.IsDigital = Regex.IsMatch(name, @"\b(digital|webrip|web)\b", RegexOptions.IgnoreCase);
        result.IsTruePdf = Regex.IsMatch(releaseName, @"\btrue[\s._\-]*pdf\b", RegexOptions.IgnoreCase);

        var fmt = FormatTokenRegex().Match(name);
        if (fmt.Success)
        {
            result.Format ??= fmt.Groups["fmt"].Value.ToUpperInvariant();
            name = name.Remove(fmt.Index, fmt.Length);
        }

        // The title is everything before the first "data" token we recognise.
        var cut = name.Length;

        void Consider(Match m)
        {
            if (m.Success && m.Index > 0 && m.Index < cut)
            {
                cut = m.Index;
            }
        }

        var week = WeekRegex().Match(name);
        var quarter = QuarterRegex().Match(name);
        var numericDate = NumericDateRegex().Match(name);
        var monthMatch = FindMonth(name);

        if (week.Success)
        {
            result.Week = int.Parse(week.Groups["week"].Value);
            result.Year = int.Parse(week.Groups["year"].Value);
            Consider(week);
        }
        else if (quarter.Success)
        {
            result.Quarter = int.Parse(quarter.Groups["q"].Value);
            result.Year = int.Parse(quarter.Groups["year"].Value);
            Consider(quarter);
        }
        else if (numericDate.Success)
        {
            result.Year = int.Parse(numericDate.Groups["year"].Value);
            result.Month = int.Parse(numericDate.Groups["month"].Value);
            Consider(numericDate);
        }
        else if (monthMatch is { } mm)
        {
            result.Month = mm.Month;
            var year = YearRegex().Match(name, mm.Index);
            if (year.Success)
            {
                result.Year = int.Parse(year.Groups["year"].Value);
            }

            if (mm.Index > 0 && mm.Index < cut)
            {
                cut = mm.Index;
            }
        }

        if (!result.IsDated)
        {
            // Look for an issue number that isn't the year.
            foreach (Match m in IssueRegex().Matches(name))
            {
                if (m.Index == 0)
                {
                    continue;
                }

                var raw = m.Groups["num"].Value;
                var isYear = raw.Length == 4 && (raw.StartsWith("19") || raw.StartsWith("20"));
                var explicitMarker = m.Value.StartsWith('#') || m.Value.StartsWith("issue", StringComparison.OrdinalIgnoreCase) ||
                                     m.Value.StartsWith("no", StringComparison.OrdinalIgnoreCase);
                if (isYear && !explicitMarker)
                {
                    continue;
                }

                result.IssueNumber = decimal.Parse(raw, CultureInfo.InvariantCulture);
                Consider(m);
                break;
            }

            var y = YearRegex().Matches(name).FirstOrDefault(m => m.Index > 0);
            if (y != null)
            {
                result.Year ??= int.Parse(y.Groups["year"].Value);
                Consider(y);
            }
        }

        var title = name[..cut];
        title = NoiseRegex().Replace(title, " ");
        title = title.Replace("(", " ").Replace(")", " ");
        title = MultiSpace().Replace(title, " ").Trim(' ', '-', '–', ',');
        if (title.Length == 0 || (title.All(char.IsDigit) && !result.IsDated && result.IssueNumber == null))
        {
            return null;
        }

        result.TitleName = title;
        return result;
    }

    /// <summary>Lower-case, alphanumeric-only form used to match a parsed title against the library.</summary>
    public static string CleanTitle(string title)
    {
        var t = title.ToLowerInvariant().Replace("&", "and");
        t = Regex.Replace(t, @"^(the|a|an)\s+", "");
        return Regex.Replace(t, @"[^a-z0-9]", "");
    }

    private static (int Month, int Index)? FindMonth(string name)
    {
        for (var i = 0; i < 12; i++)
        {
            foreach (var candidate in new[] { MonthNames[i], MonthAbbrevs[i], i == 8 ? "Sept" : null })
            {
                if (candidate == null)
                {
                    continue;
                }

                var m = Regex.Match(name, $@"\b{candidate}\b\.?(?=\s+(?:19|20)\d{{2}}\b|\s*\(?(?:19|20)\d{{2}})", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    return (i + 1, m.Index);
                }
            }
        }

        return null;
    }
}
