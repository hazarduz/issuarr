using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Issuarr.Core.Indexers;
using Issuarr.Core.Providers;

namespace Issuarr.Core.DownloadClients;

/// <summary>Transmission RPC. Issuarr's torrents go in a sub-folder named after the category (and get a label on 4.x).</summary>
public class Transmission(IHttpClientFactory http, TorrentFetcher fetcher) : DownloadClientBase(http)
{
    private string _sessionId = "";

    public override string Implementation => "Transmission";
    public override string DisplayName => "Transmission";
    public override string Description => "Torrent · downloads go to a sub-folder named after the category";
    public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
    protected override int DefaultPort => 9091;
    protected override string DefaultUrlBase => "transmission";

    public override async Task<string> Add(ProviderDefinition def, ReleaseInfo release, CancellationToken ct)
    {
        var torrent = await fetcher.Fetch(release, ct);
        var session = await Rpc(def, "session-get", new JsonObject(), ct);
        var baseDir = ((string?)session["download-dir"] ?? "").TrimEnd('/');

        var args = new JsonObject
        {
            ["download-dir"] = $"{baseDir}/{Category(def)}",
            ["labels"] = new JsonArray(Category(def)),
        };
        if (torrent.Magnet != null)
        {
            args["filename"] = torrent.Magnet;
        }
        else
        {
            args["metainfo"] = Convert.ToBase64String(torrent.TorrentFile!);
        }

        var result = await Rpc(def, "torrent-add", args, ct);
        var added = result["torrent-added"] ?? result["torrent-duplicate"];
        return ((string?)added?["hashString"] ?? torrent.Hash).ToLowerInvariant();
    }

    public override async Task<List<DownloadItem>> GetItems(ProviderDefinition def, CancellationToken ct)
    {
        var result = await Rpc(def, "torrent-get", new JsonObject
        {
            ["fields"] = new JsonArray("hashString", "name", "totalSize", "leftUntilDone", "eta", "status", "error", "errorString", "downloadDir", "labels", "percentDone"),
        }, ct);

        var category = Category(def);
        var items = new List<DownloadItem>();
        foreach (var t in result["torrents"]?.AsArray() ?? [])
        {
            var dir = ((string?)t!["downloadDir"] ?? "").TrimEnd('/');
            var labels = t["labels"]?.AsArray().Select(l => (string?)l).ToList() ?? [];
            if (!labels.Contains(category) && !dir.EndsWith("/" + category, StringComparison.Ordinal))
            {
                continue;
            }

            var left = (long?)t["leftUntilDone"] ?? 0;
            var status = (int?)t["status"] ?? 0;
            var error = (int?)t["error"] ?? 0;
            var eta = (long?)t["eta"] ?? -1;
            var name = (string?)t["name"] ?? "";

            items.Add(new DownloadItem
            {
                DownloadId = ((string?)t["hashString"] ?? "").ToLowerInvariant(),
                Title = name,
                TotalSize = (long?)t["totalSize"] ?? 0,
                RemainingSize = left,
                Eta = eta > 0 ? TimeSpan.FromSeconds(eta) : null,
                Status = error != 0 ? DownloadItemStatus.Warning
                    : left == 0 && (double?)t["percentDone"] >= 1 ? DownloadItemStatus.Completed
                    : status == 0 ? DownloadItemStatus.Paused
                    : status is 1 or 2 or 3 ? DownloadItemStatus.Queued
                    : DownloadItemStatus.Downloading,
                Message = error != 0 ? (string?)t["errorString"] : null,
                OutputPath = MapPath(def, $"{dir}/{name}"),
            });
        }

        return items;
    }

    public override Task Remove(ProviderDefinition def, string downloadId, bool deleteData, CancellationToken ct) =>
        Rpc(def, "torrent-remove", new JsonObject { ["ids"] = new JsonArray(downloadId), ["delete-local-data"] = deleteData }, ct);

    public override Task Test(ProviderDefinition def, CancellationToken ct) => Rpc(def, "session-get", new JsonObject(), ct);

    private async Task<JsonNode> Rpc(ProviderDefinition def, string method, JsonObject args, CancellationToken ct)
    {
        var payload = new JsonObject { ["method"] = method, ["arguments"] = args }.ToJsonString();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Url(def, "rpc")) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
            req.Headers.Add("X-Transmission-Session-Id", _sessionId);
            if (def.Get("username").Length > 0)
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{def.Get("username")}:{def.Get("password")}")));
            }

            HttpResponseMessage res;
            try
            {
                res = await HttpFactory.CreateClient(Http.Client).SendAsync(req, ct);
            }
            catch (HttpRequestException e)
            {
                throw Unreachable("Transmission", e);
            }

            using (res)
            {
                if (res.StatusCode == HttpStatusCode.Conflict && res.Headers.TryGetValues("X-Transmission-Session-Id", out var ids))
                {
                    _sessionId = ids.First();
                    continue;
                }

                var body = JsonNode.Parse(await Http.ReadOrThrow(res, "Transmission", ct))!;
                var result = (string?)body["result"];
                if (result != "success")
                {
                    throw new ProviderTestException($"Transmission: {result}");
                }

                return body["arguments"] ?? new JsonObject();
            }
        }

        throw new ProviderTestException("Transmission session negotiation failed");
    }
}
