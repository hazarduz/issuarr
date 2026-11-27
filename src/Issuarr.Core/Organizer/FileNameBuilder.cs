using System.Globalization;
using System.Text.RegularExpressions;
using Issuarr.Core.Configuration;
using Issuarr.Core.Issues;
using Issuarr.Core.Titles;

namespace Issuarr.Core.Organizer;

/// <summary>
/// Builds file and folder names from the naming formats in Settings → Media Management.
/// Tokens: {Title} {Title Year} {Publisher} {Issue} {Issue:000} {Issue Title}
/// {Release Date} {Release Date:yyyy-MM} {Release Year} {Format}
/// </summary>
public partial class FileNameBuilder(IConfigService config)
{
    public static readonly string[] Tokens =
        ["{Title}", "{Author}", "{Title Year}", "{Publisher}", "{Issue}", "{Issue:000}", "{Issue Title}", "{Release Date}", "{Release Date:yyyy-MM}", "{Release Year}", "{Format}"];

    public string IssueFileName(Title title, Issue issue, string extension)
    {
        var format = title.Kind switch
        {
            TitleKind.Comic => config.ComicIssueFormat,
            TitleKind.Book => config.BookFileFormat,
            _ => config.MagazineIssueFormat,
        };
        return Build(format, title, issue, extension.TrimStart('.').ToUpperInvariant()) + "." + extension.TrimStart('.').ToLowerInvariant();
    }

    public string TitleFolder(Title title) => Build(title.Kind == TitleKind.Book ? config.AuthorFolderFormat : config.TitleFolderFormat, title, null, null);

    public static string Build(string format, Title title, Issue? issue, string? fileFormat)
    {
        var result = TokenRegex().Replace(format, m =>
        {
            var token = m.Groups["token"].Value.Trim().ToLowerInvariant();
            var spec = m.Groups["spec"].Success ? m.Groups["spec"].Value : null;
            return token switch
            {
                "title" or "author" => title.Name,
                "title year" => title.Year.ToString(CultureInfo.InvariantCulture),
                "publisher" => title.Publisher ?? "",
                "issue" => IssueToken(issue, spec),
                "issue title" => issue?.IssueTitle ?? "",
                "release date" => DateToken(issue, spec ?? "yyyy-MM-dd"),
                "release year" => DateToken(issue, "yyyy"),
                "format" => fileFormat ?? "",
                _ => m.Value,
            };
        });

        // Tidy up left-overs from empty tokens: "()", "[]", " - " at the ends, double spaces.
        result = EmptyBrackets().Replace(result, "");
        result = MultiSpace().Replace(result, " ").Trim(' ', '-', '.', '_');
        return Clean(result);
    }

    public static string Clean(string name)
    {
        var cleaned = name.Replace(":", " -").Replace("/", "-").Replace("\\", "-");
        cleaned = InvalidChars().Replace(cleaned, "");
        return MultiSpace().Replace(cleaned, " ").Trim().TrimEnd('.');
    }

    private static string IssueToken(Issue? issue, string? spec)
    {
        if (issue == null)
        {
            return "";
        }

        if (IssueNumbering.GetNumber(issue.Number) is { } n)
        {
            if (spec != null && spec.All(c => c == '0'))
            {
                var whole = Math.Truncate(n);
                var frac = n - whole;
                var padded = ((long)whole).ToString(new string('0', spec.Length), CultureInfo.InvariantCulture);
                return frac == 0 ? padded : padded + frac.ToString(".##", CultureInfo.InvariantCulture);
            }

            return n.ToString("0.##", CultureInfo.InvariantCulture);
        }

        return issue.Number;
    }

    private static string DateToken(Issue? issue, string spec) =>
        issue != null && DateOnly.TryParse(issue.ReleaseDate, CultureInfo.InvariantCulture, out var d)
            ? d.ToString(spec, CultureInfo.InvariantCulture)
            : "";

    [GeneratedRegex(@"\{(?<token>[^}:]+)(?::(?<spec>[^}]+))?\}")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"\(\s*\)|\[\s*\]")]
    private static partial Regex EmptyBrackets();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();

    [GeneratedRegex(@"[<>""|?*\x00-\x1F]")]
    private static partial Regex InvalidChars();
}
