using System.Text.Json.Nodes;
using Issuarr.Core.Indexers;
using Issuarr.Core.Providers;

namespace Issuarr.Core.DownloadClients;

public class Sabnzbd(IHttpClientFactory http) : DownloadClientBase(http)
{
    public override string Implementation => "Sabnzbd";
    public override string DisplayName => "SABnzbd";
    public override string Description => "Usenet · uses a category to track Issuarr's downloads";
    public override DownloadProtocol Protocol => DownloadProtocol.Usenet;
    protected override int DefaultPort => 8080;
    protected override bool HasUsername => false;
    protected override IEnumerable<ProviderField> ExtraAuthFields => [new("apiKey", "API Key", FieldType.Password, "SABnzbd → Config → General → API Key", Required: true)];

    public override async Task<string> Add(ProviderDefinition def, ReleaseInfo release, CancellationToken ct)
    {
        var result = await Api(def, $"mode=addurl&name={Uri.EscapeDataString(release.DownloadUrl)}&nzbname={Uri.EscapeDataString(release.Title)}&cat={Uri.EscapeDataString(Category(def))}", ct);
        var id = (string?)result["nzo_ids"]?.AsArray().FirstOrDefault();
        return id ?? throw new InvalidOperationException("SABnzbd didn't accept the NZB");
    }

    public override async Task<List<DownloadItem>> GetItems(ProviderDefinition def, CancellationToken ct)
    {
        var category = Category(def);
        var items = new List<DownloadItem>();

        var queue = await Api(def, "mode=queue&limit=200", ct);
        foreach (var s in queue["queue"]?["slots"]?.AsArray() ?? [])
        {
            if (!string.Equals((string?)s!["cat"], category, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var mb = double.TryParse((string?)s["mb"], System.Globalization.CultureInfo.InvariantCulture, out var m) ? m : 0;
            var mbLeft = double.TryParse((string?)s["mbleft"], System.Globalization.CultureInfo.InvariantCulture, out var l) ? l : 0;
            var status = (string?)s["status"] ?? "";
            items.Add(new DownloadItem
            {
                DownloadId = (string?)s["nzo_id"] ?? "",
                Title = (string?)s["filename"] ?? "",
                TotalSize = (long)(mb * 1024 * 1024),
                RemainingSize = (long)(mbLeft * 1024 * 1024),
                Eta = TimeSpan.TryParse((string?)s["timeleft"], out var eta) && eta > TimeSpan.Zero ? eta : null,
                Status = status switch
                {
                    "Paused" => DownloadItemStatus.Paused,
                    "Queued" or "Fetching" or "Grabbing" or "Propagating" => DownloadItemStatus.Queued,
                    _ => DownloadItemStatus.Downloading,
                },
            });
        }

        var history = await Api(def, $"mode=history&limit=100&category={Uri.EscapeDataString(category)}", ct);
        foreach (var s in history["history"]?["slots"]?.AsArray() ?? [])
        {
            var status = (string?)s!["status"] ?? "";
            var bytes = (long?)s["bytes"] ?? 0;
            items.Add(new DownloadItem
            {
                DownloadId = (string?)s["nzo_id"] ?? "",
                Title = (string?)s["name"] ?? "",
                TotalSize = bytes,
                RemainingSize = 0,
                Status = status switch
                {
                    "Completed" => DownloadItemStatus.Completed,
                    "Failed" => DownloadItemStatus.Failed,
                    _ => DownloadItemStatus.Downloading, // Verifying, Repairing, Extracting, Moving…
                },
                Message = status == "Failed" ? (string?)s["fail_message"] : status is "Completed" ? null : status,
                OutputPath = MapPath(def, (string?)s["storage"]),
            });
        }

        return items;
    }

    public override async Task Remove(ProviderDefinition def, string downloadId, bool deleteData, CancellationToken ct)
    {
        var del = deleteData ? "1" : "0";
        await Api(def, $"mode=queue&name=delete&value={Uri.EscapeDataString(downloadId)}&del_files={del}", ct);
        await Api(def, $"mode=history&name=delete&value={Uri.EscapeDataString(downloadId)}&del_files={del}", ct);
    }

    public override async Task Test(ProviderDefinition def, CancellationToken ct)
    {
        await Api(def, "mode=queue&limit=1", ct);
        var cats = await Api(def, "mode=get_cats", ct);
        var list = cats["categories"]?.AsArray().Select(c => (string?)c).ToList() ?? [];
        if (!list.Contains(Category(def), StringComparer.OrdinalIgnoreCase))
        {
            throw new ProviderTestException($"SABnzbd has no category called \"{Category(def)}\". Add it in SABnzbd → Config → Categories.");
        }
    }

    private async Task<JsonNode> Api(ProviderDefinition def, string query, CancellationToken ct)
    {
        var url = Url(def, "api") + $"?{query}&output=json&apikey={Uri.EscapeDataString(def.Get("apiKey"))}";
        HttpResponseMessage res;
        try
        {
            res = await HttpFactory.CreateClient(Http.Client).GetAsync(url, ct);
        }
        catch (HttpRequestException e)
        {
            throw Unreachable("SABnzbd", e);
        }

        using (res)
        {
            var body = JsonNode.Parse(await Http.ReadOrThrow(res, "SABnzbd", ct))!;
            if (body["error"] is { } err)
            {
                throw new ProviderTestException($"SABnzbd: {(string?)err}");
            }

            if (body["status"] is JsonValue v && v.TryGetValue<bool>(out var ok) && !ok)
            {
                throw new ProviderTestException("SABnzbd rejected the request");
            }

            return body;
        }
    }
}
