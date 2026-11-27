using System.Globalization;
using System.Xml.Linq;
using Issuarr.Core.Providers;

namespace Issuarr.Core.Indexers;

/// <summary>
/// Newznab (usenet) and Torznab (torrent) share one API: RSS XML with extra
/// &lt;newznab:attr&gt; / &lt;torznab:attr&gt; elements. Prowlarr and Jackett expose
/// every indexer as a Torznab/Newznab feed, so this covers most setups.
/// </summary>
public abstract class NewznabBase(IHttpClientFactory http) : IIndexer
{
    public abstract string Implementation { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public abstract DownloadProtocol Protocol { get; }

    public virtual IReadOnlyList<ProviderField> Fields =>
    [
        new("baseUrl", "URL", FieldType.Url, "e.g. http://prowlarr:9696/1/ or https://api.nzbgeek.info", Required: true),
        new("apiPath", "API Path", HelpText: "Usually /api", Default: "/api", Advanced: true),
        new("apiKey", "API Key", FieldType.Password),
        new("categories", "Categories", HelpText: "Comma separated. 7030 = Comics, 7010 = Magazines, 7020 = eBooks", Default: Categories.Default),
    ];

    public Task<List<ReleaseInfo>> Search(ProviderDefinition def, string query, CancellationToken ct) =>
        Fetch(def, new() { ["t"] = "search", ["q"] = query }, ct);

    public Task<List<ReleaseInfo>> Recent(ProviderDefinition def, CancellationToken ct) =>
        Fetch(def, new() { ["t"] = "search" }, ct);

    public async Task Test(ProviderDefinition def, CancellationToken ct)
    {
        var caps = await Get(def, new() { ["t"] = "caps" }, ct);
        ThrowOnError(caps);
        if (caps.Root?.Name.LocalName != "caps")
        {
            throw new ProviderTestException("The URL didn't return Newznab/Torznab capabilities. Check the URL and API path.");
        }

        await Recent(def, ct);
    }

    private async Task<List<ReleaseInfo>> Fetch(ProviderDefinition def, Dictionary<string, string> args, CancellationToken ct)
    {
        args["cat"] = Categories.Effective(def.Get("categories", Categories.Default));
        args["extended"] = "1";
        args["limit"] = "100";
        var doc = await Get(def, args, ct);
        ThrowOnError(doc);
        return Parse(doc, def, Protocol);
    }

    private async Task<XDocument> Get(ProviderDefinition def, Dictionary<string, string> args, CancellationToken ct)
    {
        var apiKey = def.Get("apiKey");
        if (apiKey.Length > 0)
        {
            args["apikey"] = apiKey;
        }

        var url = Http.Combine(def.Get("baseUrl"), def.Get("apiPath", "/api")) + "?" +
                  string.Join("&", args.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        using var res = await http.CreateClient(Http.Client).GetAsync(url, ct);
        var body = await Http.ReadOrThrow(res, def.Name, ct);
        try
        {
            return XDocument.Parse(body);
        }
        catch (System.Xml.XmlException)
        {
            throw new ProviderTestException($"{def.Name}: response wasn't valid XML. Is this a Newznab/Torznab URL?");
        }
    }

    private static void ThrowOnError(XDocument doc)
    {
        if (doc.Root?.Name.LocalName == "error")
        {
            var code = doc.Root.Attribute("code")?.Value;
            var msg = doc.Root.Attribute("description")?.Value ?? "Unknown error";
            throw new ProviderTestException(code is "100" or "101" or "102" ? $"Invalid API key: {msg}" : msg);
        }
    }

    public static List<ReleaseInfo> Parse(XDocument doc, ProviderDefinition def, DownloadProtocol protocol)
    {
        var results = new List<ReleaseInfo>();
        foreach (var item in doc.Descendants("item"))
        {
            var attrs = item.Elements()
                .Where(e => e.Name.LocalName == "attr")
                .GroupBy(e => e.Attribute("name")?.Value ?? "")
                .ToDictionary(g => g.Key, g => g.First().Attribute("value")?.Value ?? "", StringComparer.OrdinalIgnoreCase);

            var enclosure = item.Element("enclosure");
            var link = enclosure?.Attribute("url")?.Value ?? item.Element("link")?.Value ?? "";
            if (attrs.TryGetValue("magneturl", out var magnet) && string.IsNullOrEmpty(link))
            {
                link = magnet;
            }

            var size = long.TryParse(attrs.GetValueOrDefault("size") ?? enclosure?.Attribute("length")?.Value ?? item.Element("size")?.Value, out var s) ? s : 0;
            DateTime? published = DateTime.TryParse(item.Element("pubDate")?.Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var p) ? p : null;

            var release = new ReleaseInfo
            {
                Guid = item.Element("guid")?.Value ?? link,
                Title = item.Element("title")?.Value?.Trim() ?? "",
                DownloadUrl = link,
                InfoUrl = item.Element("comments")?.Value ?? item.Element("guid")?.Value,
                Size = size,
                PublishDate = published,
                Indexer = def.Name,
                IndexerId = def.Id,
                IndexerPriority = def.Priority,
                Protocol = protocol,
                InfoHash = attrs.GetValueOrDefault("infohash"),
            };

            if (protocol == DownloadProtocol.Torrent)
            {
                release.Seeders = int.TryParse(attrs.GetValueOrDefault("seeders"), out var se) ? se : null;
                release.Peers = int.TryParse(attrs.GetValueOrDefault("peers"), out var pe) ? pe : null;
            }

            if (release.Title.Length > 0 && release.DownloadUrl.Length > 0)
            {
                results.Add(release);
            }
        }

        return results;
    }
}

public class NewznabIndexer(IHttpClientFactory http) : NewznabBase(http)
{
    public override string Implementation => "Newznab";
    public override string DisplayName => "Newznab";
    public override string Description => "Usenet indexers (NZBgeek, NZBplanet, DrunkenSlug…) or a Prowlarr/NZBHydra Newznab feed";
    public override DownloadProtocol Protocol => DownloadProtocol.Usenet;
}

public class TorznabIndexer(IHttpClientFactory http) : NewznabBase(http)
{
    public override string Implementation => "Torznab";
    public override string DisplayName => "Torznab";
    public override string Description => "Torrent indexers via a Prowlarr or Jackett Torznab feed";
    public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
}
