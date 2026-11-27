using System.Text.Json;
using Issuarr.Core.Providers;

namespace Issuarr.Core.Indexers;

/// <summary>
/// Searches every indexer in Prowlarr through Prowlarr's own search API, so one
/// entry here covers all of them (torrent and usenet).
/// </summary>
public class ProwlarrIndexer(IHttpClientFactory http) : IIndexer
{
    public string Implementation => "Prowlarr";
    public string DisplayName => "Prowlarr";
    public string Description => "Search all of your Prowlarr indexers at once";
    public DownloadProtocol Protocol => DownloadProtocol.Unknown;

    public IReadOnlyList<ProviderField> Fields =>
    [
        new("baseUrl", "Prowlarr URL", FieldType.Url, "e.g. http://prowlarr:9696", "http://localhost:9696", Required: true),
        new("apiKey", "API Key", FieldType.Password, "Prowlarr → Settings → General → API Key", Required: true),
        new("categories", "Categories", HelpText: "7030 = Comics, 7010 = Magazines, 7020 = eBooks", Default: Categories.Default),
        new("indexerIds", "Indexer IDs", HelpText: "Optional. Comma separated Prowlarr indexer IDs to limit the search to", Advanced: true),
    ];

    public Task<List<ReleaseInfo>> Search(ProviderDefinition def, string query, CancellationToken ct) => Fetch(def, query, ct);

    public Task<List<ReleaseInfo>> Recent(ProviderDefinition def, CancellationToken ct) => Fetch(def, "", ct);

    public async Task Test(ProviderDefinition def, CancellationToken ct)
    {
        using var req = Request(def, "api/v1/system/status");
        using var res = await http.CreateClient(Http.Client).SendAsync(req, ct);
        var body = await Http.ReadOrThrow(res, "Prowlarr", ct);
        if (!body.Contains("\"appName\"", StringComparison.Ordinal))
        {
            throw new ProviderTestException("That URL doesn't look like Prowlarr");
        }
    }

    private async Task<List<ReleaseInfo>> Fetch(ProviderDefinition def, string query, CancellationToken ct)
    {
        var qs = new List<string> { "type=search", "limit=100", "query=" + Uri.EscapeDataString(query) };
        qs.AddRange(Categories.Effective(def.Get("categories", Categories.Default)).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => "categories=" + c));
        qs.AddRange(def.Get("indexerIds").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(i => "indexerIds=" + i));

        using var req = Request(def, "api/v1/search?" + string.Join("&", qs));
        using var res = await http.CreateClient(Http.Client).SendAsync(req, ct);
        return Parse(await Http.ReadOrThrow(res, "Prowlarr", ct), def);
    }

    private static HttpRequestMessage Request(ProviderDefinition def, string path)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, Http.Combine(def.Get("baseUrl"), path));
        req.Headers.Add("X-Api-Key", def.Get("apiKey"));
        return req;
    }

    public static List<ReleaseInfo> Parse(string json, ProviderDefinition def)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ReleaseInfo>();
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            string? Str(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            int? Int(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

            var protocol = Str("protocol") switch
            {
                "torrent" => DownloadProtocol.Torrent,
                "usenet" => DownloadProtocol.Usenet,
                _ => DownloadProtocol.Unknown,
            };
            var url = Str("downloadUrl") ?? Str("magnetUrl") ?? "";
            var title = Str("title") ?? "";
            if (url.Length == 0 || title.Length == 0 || protocol == DownloadProtocol.Unknown)
            {
                continue;
            }

            list.Add(new ReleaseInfo
            {
                Guid = Str("guid") ?? url,
                Title = title,
                DownloadUrl = url,
                InfoUrl = Str("infoUrl"),
                Size = r.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0,
                PublishDate = DateTime.TryParse(Str("publishDate"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : null,
                Indexer = $"{Str("indexer") ?? "Prowlarr"} (Prowlarr)",
                IndexerId = def.Id,
                IndexerPriority = def.Priority,
                Protocol = protocol,
                Seeders = protocol == DownloadProtocol.Torrent ? Int("seeders") : null,
                Peers = protocol == DownloadProtocol.Torrent ? Int("leechers") : null,
                InfoHash = Str("infoHash"),
            });
        }

        return list;
    }
}
