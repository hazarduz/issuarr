using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Issuarr.Core.Configuration;
using Issuarr.Core.Providers;

namespace Issuarr.Core.Metadata;

public class SeriesInfo
{
    public string Source { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string Name { get; set; } = "";
    public int? Year { get; set; }
    public string? Publisher { get; set; }
    public int IssueCount { get; set; }
    public string? Overview { get; set; }
    public string? CoverUrl { get; set; }
    public string? SiteUrl { get; set; }

    /// <summary>Magazines: release frequency when the source knows it (e.g. "Monthly").</summary>
    public string? Frequency { get; set; }
}

public class IssueInfo
{
    public string ExternalId { get; set; } = "";
    public string Number { get; set; } = "";
    public string? Name { get; set; }
    public string? CoverDate { get; set; }
    public string? StoreDate { get; set; }
    public string? CoverUrl { get; set; }
}

/// <summary>
/// A metadata source. For comics and magazines a "series" is the title and its issues are
/// issues; for eBooks the series is an author and the "issues" are their books.
/// </summary>
public interface IMetadataProvider
{
    string Source { get; }
    string DisplayName { get; }
    IReadOnlyList<Titles.TitleKind> Kinds { get; }
    bool IsConfigured { get; }
    Task<List<SeriesInfo>> Search(string query, CancellationToken ct);
    Task<SeriesInfo> GetSeries(string externalId, CancellationToken ct);
    Task<List<IssueInfo>> GetIssues(string externalId, CancellationToken ct);
}

public class MetadataException(string message) : Exception(message);

/// <summary>ComicVine (comicvine.gamespot.com). Needs a free API key.</summary>
public partial class ComicVine(IHttpClientFactory http, IConfigService config) : IMetadataProvider
{
    // Overridable for testing or a caching proxy.
    private static readonly string Api = (Environment.GetEnvironmentVariable("ISSUARR_COMICVINE_URL") ?? "https://comicvine.gamespot.com/api").TrimEnd('/') + "/";
    private static readonly SemaphoreSlim Throttle = new(1, 1);
    private static DateTime _last = DateTime.MinValue;

    public string Source => "comicvine";
    public string DisplayName => "ComicVine";
    public IReadOnlyList<Titles.TitleKind> Kinds => [Titles.TitleKind.Comic, Titles.TitleKind.Magazine];
    public bool IsConfigured => config.ComicVineApiKey.Length > 0;

    public async Task<List<SeriesInfo>> Search(string query, CancellationToken ct)
    {
        var json = await Get($"search/?resources=volume&limit=20&query={Uri.EscapeDataString(query)}&field_list=id,name,start_year,publisher,image,count_of_issues,deck,description,site_detail_url", ct);
        return json["results"]?.AsArray().Select(v => ToSeries(v!)).ToList() ?? [];
    }

    public async Task<SeriesInfo> GetSeries(string externalId, CancellationToken ct)
    {
        var json = await Get($"volume/4050-{Uri.EscapeDataString(externalId)}/?field_list=id,name,start_year,publisher,image,count_of_issues,deck,description,site_detail_url", ct);
        return ToSeries(json["results"]!);
    }

    public async Task<List<IssueInfo>> GetIssues(string externalId, CancellationToken ct)
    {
        var all = new List<IssueInfo>();
        for (var offset = 0; ; offset += 100)
        {
            var json = await Get($"issues/?filter=volume:{Uri.EscapeDataString(externalId)}&limit=100&offset={offset}&field_list=id,issue_number,name,cover_date,store_date,image", ct);
            var page = json["results"]?.AsArray() ?? [];
            all.AddRange(page.Select(i => new IssueInfo
            {
                ExternalId = ((int?)i!["id"])?.ToString() ?? "",
                Number = (string?)i["issue_number"] ?? "",
                Name = (string?)i["name"],
                CoverDate = (string?)i["cover_date"],
                StoreDate = (string?)i["store_date"],
                CoverUrl = (string?)i["image"]?["medium_url"],
            }));

            var total = (int?)json["number_of_total_results"] ?? 0;
            if (page.Count == 0 || offset + 100 >= total)
            {
                break;
            }
        }

        return all;
    }

    private static SeriesInfo ToSeries(JsonNode v) => new()
    {
        Source = "comicvine",
        ExternalId = ((int?)v["id"])?.ToString() ?? "",
        Name = (string?)v["name"] ?? "",
        Year = int.TryParse((string?)v["start_year"], out var y) ? y : null,
        Publisher = (string?)v["publisher"]?["name"],
        IssueCount = (int?)v["count_of_issues"] ?? 0,
        Overview = (string?)v["deck"] is { Length: > 0 } deck ? deck : StripHtml((string?)v["description"]),
        CoverUrl = (string?)v["image"]?["medium_url"],
        SiteUrl = (string?)v["site_detail_url"],
    };

    private async Task<JsonNode> Get(string pathAndQuery, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new MetadataException("Add your ComicVine API key in Settings → General to search for comics.");
        }

        // ComicVine allows ~1 request per second; be polite.
        await Throttle.WaitAsync(ct);
        try
        {
            var wait = _last.AddSeconds(1) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }

            var url = Api + pathAndQuery + (pathAndQuery.Contains('?') ? "&" : "?") + $"format=json&api_key={Uri.EscapeDataString(config.ComicVineApiKey)}";
            using var res = await http.CreateClient(Http.Client).GetAsync(url, ct);
            _last = DateTime.UtcNow;
            var body = await res.Content.ReadAsStringAsync(ct);
            JsonNode? json;
            try
            {
                json = JsonNode.Parse(body);
            }
            catch (System.Text.Json.JsonException)
            {
                throw new MetadataException($"ComicVine returned HTTP {(int)res.StatusCode} with an unexpected response");
            }

            var status = (int?)json?["status_code"];
            if (status == 100)
            {
                throw new MetadataException("ComicVine says the API key is invalid. Check it in Settings → General.");
            }

            if (status == 107)
            {
                throw new MetadataException("ComicVine rate limit reached. Try again in a few minutes.");
            }

            if (status != 1)
            {
                throw new MetadataException($"ComicVine error: {(string?)json?["error"] ?? res.ReasonPhrase}");
            }

            return json!;
        }
        finally
        {
            Throttle.Release();
        }
    }

    public static string? StripHtml(string? html) =>
        html == null ? null : WebUtilityDecode(HtmlTags().Replace(html, " ")).Trim() is { Length: > 0 } s ? (s.Length > 600 ? s[..600] + "…" : s) : null;

    private static string WebUtilityDecode(string s) => System.Net.WebUtility.HtmlDecode(Regex.Replace(s, @"\s{2,}", " "));

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTags();
}

/// <summary>Metron (metron.cloud). Free account; uses your username and password.</summary>
public class Metron(IHttpClientFactory http, IConfigService config) : IMetadataProvider
{
    private const string Api = "https://metron.cloud/api/";

    public string Source => "metron";
    public string DisplayName => "Metron";
    public IReadOnlyList<Titles.TitleKind> Kinds => [Titles.TitleKind.Comic];
    public bool IsConfigured => config.MetronUsername.Length > 0 && config.MetronPassword.Length > 0;

    public async Task<List<SeriesInfo>> Search(string query, CancellationToken ct)
    {
        var json = await Get($"series/?name={Uri.EscapeDataString(query)}", ct);
        return json["results"]?.AsArray().Select(s =>
        {
            var display = (string?)s!["series"] ?? "";
            return new SeriesInfo
            {
                Source = "metron",
                ExternalId = ((int?)s["id"])?.ToString() ?? "",
                Name = Regex.Replace(display, @"\s*\(\d{4}\)\s*$", ""),
                Year = (int?)s["year_began"],
                IssueCount = (int?)s["issue_count"] ?? 0,
            };
        }).ToList() ?? [];
    }

    public async Task<SeriesInfo> GetSeries(string externalId, CancellationToken ct)
    {
        var s = await Get($"series/{Uri.EscapeDataString(externalId)}/", ct);
        return new SeriesInfo
        {
            Source = "metron",
            ExternalId = externalId,
            Name = (string?)s["name"] ?? "",
            Year = (int?)s["year_began"],
            Publisher = (string?)s["publisher"]?["name"],
            IssueCount = (int?)s["issue_count"] ?? 0,
            Overview = (string?)s["desc"],
            SiteUrl = (string?)s["resource_url"],
        };
    }

    public async Task<List<IssueInfo>> GetIssues(string externalId, CancellationToken ct)
    {
        var all = new List<IssueInfo>();
        string? next = $"issue/?series_id={Uri.EscapeDataString(externalId)}";
        while (next != null)
        {
            var json = await Get(next, ct);
            all.AddRange(json["results"]?.AsArray().Select(i => new IssueInfo
            {
                ExternalId = ((int?)i!["id"])?.ToString() ?? "",
                Number = (string?)i["number"] ?? "",
                CoverDate = (string?)i["cover_date"],
                StoreDate = (string?)i["store_date"],
                CoverUrl = (string?)i["image"],
            }) ?? []);
            next = (string?)json["next"];
        }

        return all;
    }

    private async Task<JsonNode> Get(string pathOrUrl, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new MetadataException("Add your Metron username and password in Settings → General.");
        }

        var url = pathOrUrl.StartsWith("http", StringComparison.Ordinal) ? pathOrUrl : Api + pathOrUrl;
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.MetronUsername}:{config.MetronPassword}")));
        using var res = await http.CreateClient(Http.Client).SendAsync(req, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new MetadataException("Metron rejected the username/password.");
        }

        if (res.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            throw new MetadataException("Metron rate limit reached. Try again later.");
        }

        if (!res.IsSuccessStatusCode)
        {
            throw new MetadataException($"Metron returned HTTP {(int)res.StatusCode}");
        }

        return JsonNode.Parse(await res.Content.ReadAsStringAsync(ct))!;
    }
}

public static class MetadataDates
{
    /// <summary>Best release date for an issue: on-sale date, else cover date, as yyyy-MM-dd.</summary>
    public static string? Pick(IssueInfo i) =>
        new[] { i.StoreDate, i.CoverDate }.FirstOrDefault(d => DateOnly.TryParse(d, CultureInfo.InvariantCulture, out _)) is { } s
            ? DateOnly.Parse(s, CultureInfo.InvariantCulture).ToString("yyyy-MM-dd")
            : null;
}
