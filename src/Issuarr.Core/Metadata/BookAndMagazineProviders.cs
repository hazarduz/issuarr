using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Issuarr.Core.Configuration;
using Issuarr.Core.Providers;
using Issuarr.Core.Titles;

namespace Issuarr.Core.Metadata;

/// <summary>A single book from a search (Add New → eBook).</summary>
public class BookResult
{
    public string Source { get; set; } = "";
    public string WorkId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string? AuthorId { get; set; }
    public int? Year { get; set; }
    public string? CoverUrl { get; set; }
    public string? Overview { get; set; }
    public string? SiteUrl { get; set; }
}

public interface IBookSearch
{
    string Source { get; }
    Task<List<BookResult>> SearchBooks(string query, CancellationToken ct);
}

internal static class Json
{
    public static async Task<JsonNode> Get(IHttpClientFactory http, string url, string source, CancellationToken ct)
    {
        using var res = await http.CreateClient(Http.Client).GetAsync(url, ct);
        if ((int)res.StatusCode == 429)
        {
            throw new MetadataException($"{source} rate limit reached. Try again in a minute.");
        }

        if (!res.IsSuccessStatusCode)
        {
            throw new MetadataException($"{source} returned HTTP {(int)res.StatusCode}");
        }

        return JsonNode.Parse(await res.Content.ReadAsStringAsync(ct)) ?? throw new MetadataException($"{source} returned an empty response");
    }

    /// <summary>"Mistborn: The Final Empire" and "Mistborn - the final empire" compare equal.</summary>
    public static string Key(string title) => Regex.Replace(title.ToLowerInvariant().Replace("&", "and"), @"[^a-z0-9]+", "");

    public static string? Date(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var m = Regex.Match(raw, @"(\d{4})(?:-(\d{2}))?(?:-(\d{2}))?");
        if (!m.Success)
        {
            return null;
        }

        var month = m.Groups[2].Success ? m.Groups[2].Value : "01";
        var day = m.Groups[3].Success ? m.Groups[3].Value : "01";
        return $"{m.Groups[1].Value}-{month}-{day}";
    }
}

/// <summary>Open Library (openlibrary.org): free, no key; authors and their works.</summary>
public class OpenLibrary(IHttpClientFactory http) : IMetadataProvider, IBookSearch
{
    private static readonly string Api = (Environment.GetEnvironmentVariable("ISSUARR_OPENLIBRARY_URL") ?? "https://openlibrary.org").TrimEnd('/');

    public string Source => "openlibrary";
    public string DisplayName => "Open Library";
    public IReadOnlyList<TitleKind> Kinds => [TitleKind.Book];
    public bool IsConfigured => true;

    public async Task<List<SeriesInfo>> Search(string query, CancellationToken ct)
    {
        var json = await Json.Get(http, $"{Api}/search/authors.json?limit=20&q={Uri.EscapeDataString(query)}", DisplayName, ct);
        return json["docs"]?.AsArray().Select(a => new SeriesInfo
        {
            Source = Source,
            ExternalId = (string?)a!["key"] ?? "",
            Name = (string?)a["name"] ?? "",
            IssueCount = (int?)a["work_count"] ?? 0,
            Overview = (string?)a["top_work"] is { } top ? $"Best known for {top}" : null,
            CoverUrl = $"https://covers.openlibrary.org/a/olid/{(string?)a["key"]}-M.jpg?default=false",
            SiteUrl = $"https://openlibrary.org/authors/{(string?)a["key"]}",
        }).Where(a => a.ExternalId.Length > 0).ToList() ?? [];
    }

    public async Task<SeriesInfo> GetSeries(string externalId, CancellationToken ct)
    {
        var a = await Json.Get(http, $"{Api}/authors/{Uri.EscapeDataString(externalId)}.json", DisplayName, ct);
        var bio = a["bio"] is JsonObject o ? (string?)o["value"] : a["bio"] is JsonValue v ? v.ToString() : null;
        var photo = a["photos"]?.AsArray().Select(p => (int?)p).FirstOrDefault(p => p > 0);
        return new SeriesInfo
        {
            Source = Source,
            ExternalId = externalId,
            Name = (string?)a["name"] ?? (string?)a["personal_name"] ?? "",
            Overview = bio is { Length: > 600 } ? bio[..600] + "…" : bio,
            CoverUrl = photo != null ? $"https://covers.openlibrary.org/a/id/{photo}-M.jpg" : null,
            SiteUrl = $"https://openlibrary.org/authors/{externalId}",
        };
    }

    public async Task<List<IssueInfo>> GetIssues(string externalId, CancellationToken ct)
    {
        var url = $"{Api}/search.json?author_key={Uri.EscapeDataString(externalId)}&limit=300&sort=old" +
                  "&fields=key,title,first_publish_year,cover_i,edition_count,language";
        var json = await Json.Get(http, url, DisplayName, ct);
        var seen = new HashSet<string>();
        var books = new List<IssueInfo>();
        foreach (var w in json["docs"]?.AsArray() ?? [])
        {
            var title = ((string?)w!["title"])?.Trim();
            var languages = w["language"]?.AsArray().Select(l => (string?)l).ToList();
            if (string.IsNullOrEmpty(title) || (languages is { Count: > 0 } && !languages.Contains("eng")) || !seen.Add(Json.Key(title)))
            {
                continue; // skip untitled works, other-language editions and duplicates
            }

            var year = (int?)w["first_publish_year"];
            books.Add(new IssueInfo
            {
                ExternalId = ((string?)w["key"] ?? "").Replace("/works/", ""),
                Number = "",
                Name = title,
                CoverDate = year != null ? $"{year}-01-01" : null,
                CoverUrl = (int?)w["cover_i"] is { } c ? $"https://covers.openlibrary.org/b/id/{c}-M.jpg" : null,
            });
        }

        return books;
    }

    public async Task<List<BookResult>> SearchBooks(string query, CancellationToken ct)
    {
        var json = await Json.Get(http, $"{Api}/search.json?limit=20&q={Uri.EscapeDataString(query)}&fields=key,title,author_name,author_key,first_publish_year,cover_i", DisplayName, ct);
        return json["docs"]?.AsArray().Select(b => new BookResult
        {
            Source = Source,
            WorkId = ((string?)b!["key"] ?? "").Replace("/works/", ""),
            Title = (string?)b["title"] ?? "",
            Author = (string?)b["author_name"]?.AsArray().FirstOrDefault(),
            AuthorId = (string?)b["author_key"]?.AsArray().FirstOrDefault(),
            Year = (int?)b["first_publish_year"],
            CoverUrl = (int?)b["cover_i"] is { } c ? $"https://covers.openlibrary.org/b/id/{c}-M.jpg" : null,
            SiteUrl = $"https://openlibrary.org{(string?)b["key"]}",
        }).Where(b => b.Title.Length > 0 && b.AuthorId != null).ToList() ?? [];
    }
}

/// <summary>
/// Google Books: great covers and descriptions. Works without a key (shared quota); a free
/// API key raises the limits. Google has no author ids, so the author's name is the id.
/// </summary>
public class GoogleBooks(IHttpClientFactory http, IConfigService config) : IMetadataProvider, IBookSearch
{
    private const string Api = "https://www.googleapis.com/books/v1/volumes";

    public string Source => "googlebooks";
    public string DisplayName => "Google Books";
    public IReadOnlyList<TitleKind> Kinds => [TitleKind.Book];
    public bool IsConfigured => true;

    public async Task<List<SeriesInfo>> Search(string query, CancellationToken ct)
    {
        var items = await Volumes($"inauthor:\"{query}\"", 0, ct);
        return items
            .SelectMany(v => ((JsonArray?)v["volumeInfo"]?["authors"])?.Select(a => (string?)a) ?? [])
            .OfType<string>()
            .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new SeriesInfo { Source = Source, ExternalId = g.Key, Name = g.Key, IssueCount = g.Count() })
            .ToList();
    }

    public async Task<SeriesInfo> GetSeries(string externalId, CancellationToken ct)
    {
        var first = (await Volumes($"inauthor:\"{externalId}\"", 0, ct)).FirstOrDefault();
        return new SeriesInfo
        {
            Source = Source,
            ExternalId = externalId,
            Name = externalId,
            CoverUrl = Thumb(first?["volumeInfo"]),
            SiteUrl = $"https://www.google.com/search?tbm=bks&q=inauthor:%22{Uri.EscapeDataString(externalId)}%22",
        };
    }

    public async Task<List<IssueInfo>> GetIssues(string externalId, CancellationToken ct)
    {
        var books = new Dictionary<string, IssueInfo>();
        for (var start = 0; start < 200; start += 40)
        {
            var page = await Volumes($"inauthor:\"{externalId}\"", start, ct);
            foreach (var v in page)
            {
                var info = v["volumeInfo"];
                var authors = ((JsonArray?)info?["authors"])?.Select(a => (string?)a).ToList() ?? [];
                var title = (string?)info?["title"];
                if (title == null || !authors.Any(a => string.Equals(a, externalId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var key = Json.Key(title);
                var date = Json.Date((string?)info?["publishedDate"]);
                // Keep the earliest edition's date for each book.
                if (books.TryGetValue(key, out var existing) && string.CompareOrdinal(existing.CoverDate ?? "9999", date ?? "9999") <= 0)
                {
                    continue;
                }

                books[key] = new IssueInfo { ExternalId = (string?)v["id"] ?? key, Number = "", Name = title, CoverDate = date, CoverUrl = Thumb(info) };
            }

            if (page.Count < 40)
            {
                break;
            }
        }

        return books.Values.OrderBy(b => b.CoverDate ?? "9999").ToList();
    }

    public async Task<List<BookResult>> SearchBooks(string query, CancellationToken ct) =>
        (await Volumes(query, 0, ct)).Select(v =>
        {
            var info = v["volumeInfo"];
            var author = (string?)info?["authors"]?.AsArray().FirstOrDefault();
            return new BookResult
            {
                Source = Source,
                WorkId = (string?)v["id"] ?? "",
                Title = (string?)info?["title"] ?? "",
                Author = author,
                AuthorId = author,
                Year = int.TryParse(((string?)info?["publishedDate"])?[..Math.Min(4, ((string?)info?["publishedDate"])?.Length ?? 0)], out var y) ? y : null,
                CoverUrl = Thumb(info),
                Overview = (string?)info?["description"] is { } d && d.Length > 400 ? d[..400] + "…" : (string?)info?["description"],
                SiteUrl = (string?)info?["infoLink"],
            };
        }).Where(b => b.Title.Length > 0 && b.Author != null).ToList();

    private async Task<List<JsonNode>> Volumes(string q, int start, CancellationToken ct)
    {
        var key = config.GoogleBooksApiKey;
        var url = $"{Api}?q={Uri.EscapeDataString(q)}&printType=books&maxResults=40&startIndex={start}" + (key.Length > 0 ? $"&key={Uri.EscapeDataString(key)}" : "");
        var json = await Json.Get(http, url, DisplayName, ct);
        return json["items"]?.AsArray().OfType<JsonNode>().ToList() ?? [];
    }

    private static string? Thumb(JsonNode? info) =>
        ((string?)info?["imageLinks"]?["thumbnail"] ?? (string?)info?["imageLinks"]?["smallThumbnail"])?.Replace("http://", "https://").Replace("&edge=curl", "");
}

/// <summary>
/// Wikidata: free, structured facts about magazines (publisher, frequency, first year,
/// description, logo/cover). There's no magazine issue database anywhere, so issues still
/// come from the release schedule.
/// </summary>
public class Wikidata(IHttpClientFactory http) : IMetadataProvider
{
    private static readonly string Api = (Environment.GetEnvironmentVariable("ISSUARR_WIKIDATA_URL") ?? "https://www.wikidata.org/w/api.php").TrimEnd('/');

    // magazine, periodical, academic/scientific journal, newspaper, daily newspaper, comics magazine, trade magazine, weekly magazine, monthly magazine
    private static readonly HashSet<string> PeriodicalClasses = ["Q41298", "Q1002697", "Q737498", "Q5633421", "Q11032", "Q1110794", "Q21114848", "Q1153191", "Q1153032", "Q12340140"];

    public string Source => "wikidata";
    public string DisplayName => "Wikidata";
    public IReadOnlyList<TitleKind> Kinds => [TitleKind.Magazine];
    public bool IsConfigured => true;

    public async Task<List<SeriesInfo>> Search(string query, CancellationToken ct)
    {
        var found = await Json.Get(http, $"{Api}?action=wbsearchentities&format=json&language=en&uselang=en&type=item&limit=30&search={Uri.EscapeDataString(query)}", DisplayName, ct);
        var ids = found["search"]?.AsArray().Select(s => (string?)s!["id"]).OfType<string>().ToList() ?? [];
        if (ids.Count == 0)
        {
            return [];
        }

        var entities = await Entities(ids, ct);
        var periodicals = entities.Where(e => Claims(e, "P31").Any(PeriodicalClasses.Contains)).ToList();
        if (periodicals.Count == 0)
        {
            periodicals = entities.Where(e => (Description(e) ?? "").Contains("magazine", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var publishers = await Labels(periodicals.SelectMany(e => Claims(e, "P123")).Distinct().ToList(), ct);
        return periodicals.Select(e => ToSeries(e, publishers)).ToList();
    }

    public async Task<SeriesInfo> GetSeries(string externalId, CancellationToken ct)
    {
        var e = (await Entities([externalId], ct)).FirstOrDefault() ?? throw new MetadataException($"Wikidata has no item {externalId}");
        return ToSeries(e, await Labels(Claims(e, "P123").ToList(), ct));
    }

    public Task<List<IssueInfo>> GetIssues(string externalId, CancellationToken ct) => Task.FromResult(new List<IssueInfo>());

    private SeriesInfo ToSeries(JsonNode e, Dictionary<string, string> publishers)
    {
        var id = (string?)e["id"] ?? "";
        var image = ClaimStrings(e, "P154").Concat(ClaimStrings(e, "P18")).FirstOrDefault();
        var start = ClaimTimes(e, "P571").Concat(ClaimTimes(e, "P577")).FirstOrDefault();
        return new SeriesInfo
        {
            Source = Source,
            ExternalId = id,
            Name = (string?)e["labels"]?["en"]?["value"] ?? id,
            Year = start,
            Publisher = Claims(e, "P123").Select(p => publishers.GetValueOrDefault(p)).FirstOrDefault(p => p != null),
            Overview = Description(e),
            CoverUrl = image != null ? $"https://commons.wikimedia.org/wiki/Special:FilePath/{Uri.EscapeDataString(image)}?width=400" : null,
            SiteUrl = ClaimStrings(e, "P856").FirstOrDefault() ?? $"https://www.wikidata.org/wiki/{id}",
            Frequency = Frequency(e),
        };
    }

    private async Task<List<JsonNode>> Entities(List<string> ids, CancellationToken ct)
    {
        var json = await Json.Get(http, $"{Api}?action=wbgetentities&format=json&languages=en&props=labels|descriptions|claims&ids={string.Join("|", ids.Take(50))}", DisplayName, ct);
        var map = json["entities"]?.AsObject();
        return ids.Select(i => map?[i]).OfType<JsonNode>().Where(e => e["missing"] == null).ToList();
    }

    private async Task<Dictionary<string, string>> Labels(List<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new();
        }

        var json = await Json.Get(http, $"{Api}?action=wbgetentities&format=json&languages=en&props=labels&ids={string.Join("|", ids.Take(50))}", DisplayName, ct);
        return json["entities"]?.AsObject()
            .Select(kv => (kv.Key, Label: (string?)kv.Value?["labels"]?["en"]?["value"]))
            .Where(x => x.Label != null)
            .ToDictionary(x => x.Key, x => x.Label!) ?? new();
    }

    private static string? Description(JsonNode e) => (string?)e["descriptions"]?["en"]?["value"];

    private static IEnumerable<JsonNode> Snaks(JsonNode e, string prop) =>
        e["claims"]?[prop]?.AsArray().Select(c => c?["mainsnak"]?["datavalue"]).OfType<JsonNode>() ?? [];

    private static IEnumerable<string> Claims(JsonNode e, string prop) =>
        Snaks(e, prop).Select(d => (string?)d["value"]?["id"]).OfType<string>();

    private static IEnumerable<string> ClaimStrings(JsonNode e, string prop) =>
        Snaks(e, prop).Select(d => d["value"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>();

    private static IEnumerable<int> ClaimTimes(JsonNode e, string prop) =>
        Snaks(e, prop).Select(d => (string?)d["value"]?["time"]).OfType<string>()
            .Select(t => int.TryParse(t.TrimStart('+').Split('-')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) ? y : 0)
            .Where(y => y > 0);

    /// <summary>Publication interval (P2896), e.g. 1 month → Monthly, 2 weeks → Fortnightly.</summary>
    public static string? Frequency(JsonNode e)
    {
        foreach (var d in Snaks(e, "P2896"))
        {
            var amount = decimal.TryParse(((string?)d["value"]?["amount"])?.TrimStart('+'), NumberStyles.Number, CultureInfo.InvariantCulture, out var a) ? a : 0;
            var unit = ((string?)d["value"]?["unit"] ?? "").Split('/').Last();
            var result = (unit, amount) switch
            {
                ("Q23387", 1) => "Weekly",
                ("Q23387", 2) or ("Q573", 14) => "Fortnightly",
                ("Q5151", 1) => "Monthly",
                ("Q5151", 2) => "Bi-monthly",
                ("Q5151", 3) => "Quarterly",
                ("Q577", 1) or ("Q5151", 12) => "Annually",
                ("Q573", 7) => "Weekly",
                _ => null,
            };
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}
