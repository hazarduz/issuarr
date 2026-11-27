using System.Text.RegularExpressions;
using Issuarr.Core.Issues;
using Issuarr.Core.Titles;

namespace Issuarr.Core.Parser;

/// <summary>
/// Matches book release names ("Brandon Sanderson - The Way of Kings (2010) [EPUB]",
/// "The.Way.of.Kings.by.Brandon.Sanderson.epub", "Sanderson, Brandon - Oathbringer")
/// to an author in the library and one of their books.
/// </summary>
public static partial class BookMatcher
{
    public static string Normalize(string s)
    {
        var t = s.ToLowerInvariant().Replace("&", " and ").Replace("'", "").Replace("’", "");
        t = NonAlnum().Replace(t, " ");
        return " " + MultiSpace().Replace(t, " ").Trim() + " ";
    }

    /// <summary>Does the release mention this author? Surname is required; the whole name is preferred.</summary>
    public static int AuthorScore(string normalizedRelease, string authorName)
    {
        var parts = Normalize(authorName).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return 0;
        }

        var surname = parts[^1];
        if (!normalizedRelease.Contains(" " + surname + " "))
        {
            return 0;
        }

        return parts.Count(p => normalizedRelease.Contains(" " + p + " "));
    }

    /// <summary>The longest book title (main title, subtitle optional) contained in the release.</summary>
    public static Issue? MatchBook(string normalizedRelease, IEnumerable<Issue> books)
    {
        Issue? best = null;
        var bestLength = 0;
        foreach (var b in books)
        {
            foreach (var candidate in Variants(b.Number))
            {
                if (candidate.Trim().Length >= 3 && normalizedRelease.Contains(candidate) && candidate.Length > bestLength)
                {
                    best = b;
                    bestLength = candidate.Length;
                }
            }
        }

        return best;
    }

    public static (Title Title, Issue Book)? Match(string release, IEnumerable<Title> authors, Func<Title, List<Issue>> booksOf)
    {
        var n = Normalize(release);
        foreach (var author in authors.Where(a => a.Kind == TitleKind.Book)
                     .Select(a => (Title: a, Score: AuthorScore(n, a.Name)))
                     .Where(x => x.Score > 0)
                     .OrderByDescending(x => x.Score)
                     .Select(x => x.Title))
        {
            if (MatchBook(n, booksOf(author)) is { } book)
            {
                return (author, book);
            }
        }

        return null;
    }

    private static IEnumerable<string> Variants(string title)
    {
        yield return Normalize(title);
        // "Mistborn: The Final Empire" → also match just "Mistborn" / "The Final Empire" is too loose, so only the main title.
        var main = title.Split([':', '(', '['], 2)[0];
        if (main.Length < title.Length)
        {
            yield return Normalize(main);
        }
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlnum();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();
}
