using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Issuarr.Core.Indexers;
using Issuarr.Core.Providers;

namespace Issuarr.Core.DownloadClients;

public class NzbGet(IHttpClientFactory http) : DownloadClientBase(http)
{
    public override string Implementation => "NzbGet";
    public override string DisplayName => "NZBGet";
    public override string Description => "Usenet · uses a category to track Issuarr's downloads";
    public override DownloadProtocol Protocol => DownloadProtocol.Usenet;
    protected override int DefaultPort => 6789;

    public override async Task<string> Add(ProviderDefinition def, ReleaseInfo release, CancellationToken ct)
    {
        var name = release.Title.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase) ? release.Title : release.Title + ".nzb";
        var id = await Call(def, "append", [name, release.DownloadUrl, Category(def), 0, false, false, "", 0, "SCORE", new JsonArray()], ct);
        var nzbId = (int?)id ?? 0;
        return nzbId > 0 ? nzbId.ToString() : throw new InvalidOperationException("NZBGet didn't accept the NZB");
    }

    public override async Task<List<DownloadItem>> GetItems(ProviderDefinition def, CancellationToken ct)
    {
        var category = Category(def);
        var items = new List<DownloadItem>();
        foreach (var g in (await Call(def, "listgroups", [0], ct))?.AsArray() ?? [])
        {
            if (!string.Equals((string?)g!["Category"], category, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var status = (string?)g["Status"] ?? "";
            items.Add(new DownloadItem
            {
                DownloadId = ((int?)g["NZBID"] ?? 0).ToString(),
                Title = (string?)g["NZBName"] ?? "",
                TotalSize = ((long?)g["FileSizeMB"] ?? 0) * 1024 * 1024,
                RemainingSize = ((long?)g["RemainingSizeMB"] ?? 0) * 1024 * 1024,
                Status = status switch
                {
                    "PAUSED" => DownloadItemStatus.Paused,
                    "QUEUED" => DownloadItemStatus.Queued,
                    _ => DownloadItemStatus.Downloading,
                },
            });
        }

        foreach (var h in (await Call(def, "history", [false], ct))?.AsArray() ?? [])
        {
            if (!string.Equals((string?)h!["Category"], category, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var status = (string?)h["Status"] ?? "";
            items.Add(new DownloadItem
            {
                DownloadId = ((int?)h["NZBID"] ?? 0).ToString(),
                Title = (string?)h["Name"] ?? "",
                TotalSize = ((long?)h["FileSizeMB"] ?? 0) * 1024 * 1024,
                Status = status.StartsWith("SUCCESS") ? DownloadItemStatus.Completed
                    : status.StartsWith("FAILURE") || status.StartsWith("DELETED") ? DownloadItemStatus.Failed
                    : DownloadItemStatus.Warning,
                Message = status.StartsWith("SUCCESS") ? null : status,
                OutputPath = MapPath(def, (string?)h["FinalDir"] is { Length: > 0 } final ? final : (string?)h["DestDir"]),
            });
        }

        return items;
    }

    public override async Task Remove(ProviderDefinition def, string downloadId, bool deleteData, CancellationToken ct)
    {
        if (!int.TryParse(downloadId, out var id))
        {
            return;
        }

        await Call(def, "editqueue", [deleteData ? "GroupFinalDelete" : "GroupDelete", "", new JsonArray(id)], ct);
        await Call(def, "editqueue", [deleteData ? "HistoryFinalDelete" : "HistoryDelete", "", new JsonArray(id)], ct);
    }

    public override Task Test(ProviderDefinition def, CancellationToken ct) => Call(def, "version", [], ct);

    private async Task<JsonNode?> Call(ProviderDefinition def, string method, JsonNode?[] args, CancellationToken ct)
    {
        var payload = new JsonObject { ["method"] = method, ["params"] = new JsonArray(args.Select(a => a?.DeepClone()).ToArray()) };
        using var req = new HttpRequestMessage(HttpMethod.Post, Url(def, "jsonrpc")) { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{def.Get("username", "nzbget")}:{def.Get("password", "tegbzn6789")}")));

        HttpResponseMessage res;
        try
        {
            res = await HttpFactory.CreateClient(Http.Client).SendAsync(req, ct);
        }
        catch (HttpRequestException e)
        {
            throw Unreachable("NZBGet", e);
        }

        using (res)
        {
            var body = JsonNode.Parse(await Http.ReadOrThrow(res, "NZBGet", ct))!;
            if (body["error"] is JsonObject err)
            {
                throw new ProviderTestException($"NZBGet: {(string?)err["message"]}");
            }

            return body["result"];
        }
    }
}
