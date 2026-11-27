using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using Issuarr.Core.Indexers;
using Issuarr.Core.Providers;

namespace Issuarr.Core.DownloadClients;

/// <summary>qBittorrent Web API v2.</summary>
public class QBittorrent(IHttpClientFactory http, TorrentFetcher fetcher) : DownloadClientBase(http)
{
    private static readonly ConcurrentDictionary<string, string> Sessions = new();

    public override string Implementation => "QBittorrent";
    public override string DisplayName => "qBittorrent";
    public override string Description => "Torrent · uses a category to track Issuarr's downloads";
    public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
    protected override int DefaultPort => 8080;

    public override async Task<string> Add(ProviderDefinition def, ReleaseInfo release, CancellationToken ct)
    {
        var torrent = await fetcher.Fetch(release, ct);
        await Send(def, HttpMethod.Post, "api/v2/torrents/createCategory", new FormUrlEncodedContent(new Dictionary<string, string> { ["category"] = Category(def), ["savePath"] = "" }), ct, allowConflict: true);

        var form = new MultipartFormDataContent { { new StringContent(Category(def)), "category" } };
        if (torrent.Magnet != null)
        {
            form.Add(new StringContent(torrent.Magnet), "urls");
        }
        else
        {
            form.Add(new ByteArrayContent(torrent.TorrentFile!), "torrents", "release.torrent");
        }

        var body = await Send(def, HttpMethod.Post, "api/v2/torrents/add", form, ct);
        if (body.Trim().Equals("Fails.", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("qBittorrent refused the torrent (it may already be added)");
        }

        return torrent.Hash;
    }

    public override async Task<List<DownloadItem>> GetItems(ProviderDefinition def, CancellationToken ct)
    {
        var json = await Send(def, HttpMethod.Get, "api/v2/torrents/info?category=" + Uri.EscapeDataString(Category(def)), null, ct);
        var items = new List<DownloadItem>();
        foreach (var t in JsonNode.Parse(json)!.AsArray())
        {
            var state = (string?)t!["state"] ?? "";
            var size = (long?)t["size"] ?? 0;
            var left = (long?)t["amount_left"] ?? 0;
            var eta = (long?)t["eta"] ?? 8640000;
            var name = (string?)t["name"] ?? "";
            var content = (string?)t["content_path"] ?? Path.Combine((string?)t["save_path"] ?? "", name);

            var (status, message) = state switch
            {
                "error" or "missingFiles" => (DownloadItemStatus.Failed, "qBittorrent reports an error for this torrent"),
                "pausedDL" or "stoppedDL" => (DownloadItemStatus.Paused, null),
                "queuedDL" or "checkingDL" or "checkingResumeData" or "allocating" => (DownloadItemStatus.Queued, null),
                "metaDL" or "forcedMetaDL" => (DownloadItemStatus.Queued, "Downloading metadata"),
                "stalledDL" => (DownloadItemStatus.Warning, "Stalled: no connections"),
                "downloading" or "forcedDL" or "moving" => (DownloadItemStatus.Downloading, null),
                _ when left == 0 => (DownloadItemStatus.Completed, null),
                _ => (DownloadItemStatus.Downloading, null),
            };

            items.Add(new DownloadItem
            {
                DownloadId = ((string?)t["hash"] ?? "").ToLowerInvariant(),
                Title = name,
                TotalSize = size,
                RemainingSize = left,
                Eta = eta is > 0 and < 8640000 ? TimeSpan.FromSeconds(eta) : null,
                Status = status,
                Message = message,
                OutputPath = MapPath(def, content),
            });
        }

        return items;
    }

    public override Task Remove(ProviderDefinition def, string downloadId, bool deleteData, CancellationToken ct) =>
        Send(def, HttpMethod.Post, "api/v2/torrents/delete",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["hashes"] = downloadId, ["deleteFiles"] = deleteData ? "true" : "false" }), ct);

    public override async Task Test(ProviderDefinition def, CancellationToken ct)
    {
        Sessions.TryRemove(Key(def), out _);
        var version = await Send(def, HttpMethod.Get, "api/v2/app/version", null, ct);
        if (!version.TrimStart().StartsWith('v'))
        {
            throw new ProviderTestException("That doesn't look like qBittorrent's Web UI");
        }
    }

    private static string Key(ProviderDefinition def) => $"{def.Id}|{def.Get("host")}|{def.Get("port")}|{def.Get("username")}";

    private async Task<string> Login(ProviderDefinition def, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Url(def, "api/v2/auth/login"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = def.Get("username"), ["password"] = def.Get("password") }),
        };
        req.Headers.Referrer = new Uri(BaseUrl(def, DefaultPort));
        HttpResponseMessage res;
        try
        {
            res = await HttpFactory.CreateClient(Http.Client).SendAsync(req, ct);
        }
        catch (HttpRequestException e)
        {
            throw Unreachable("qBittorrent", e);
        }

        using (res)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            if (res.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new ProviderTestException("qBittorrent has banned this IP after too many failed logins. Restart qBittorrent or wait.");
            }

            var cookie = res.Headers.TryGetValues("Set-Cookie", out var values)
                ? values.Select(v => v.Split(';')[0]).FirstOrDefault(v => v.StartsWith("SID=", StringComparison.Ordinal) || v.StartsWith("QBT_SID", StringComparison.Ordinal))
                : null;

            // With "Bypass authentication for clients on localhost/whitelisted subnets" no cookie is issued; that's fine.
            if (cookie == null && !body.Contains("Ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new ProviderTestException("qBittorrent login failed. Check the username and password.");
            }

            return cookie ?? "";
        }
    }

    private async Task<string> Send(ProviderDefinition def, HttpMethod method, string path, HttpContent? content, CancellationToken ct, bool allowConflict = false)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var key = Key(def);
            if (!Sessions.TryGetValue(key, out var cookie))
            {
                cookie = await Login(def, ct);
                Sessions[key] = cookie;
            }

            using var req = new HttpRequestMessage(method, Url(def, path)) { Content = content };
            if (cookie.Length > 0)
            {
                req.Headers.Add("Cookie", cookie);
            }

            HttpResponseMessage res;
            try
            {
                res = await HttpFactory.CreateClient(Http.Client).SendAsync(req, ct);
            }
            catch (HttpRequestException e)
            {
                throw Unreachable("qBittorrent", e);
            }

            using (res)
            {
                if (res.StatusCode == HttpStatusCode.Forbidden && attempt == 0)
                {
                    Sessions.TryRemove(key, out _);
                    continue;
                }

                if (allowConflict && res.StatusCode == HttpStatusCode.Conflict)
                {
                    return "";
                }

                return await Http.ReadOrThrow(res, "qBittorrent", ct);
            }
        }

        throw new ProviderTestException("qBittorrent rejected the session");
    }
}
