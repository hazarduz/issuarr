using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using Issuarr.Core.Indexers;
using Issuarr.Core.Providers;

namespace Issuarr.Core.DownloadClients;

/// <summary>Deluge Web UI JSON-RPC. Uses the Label plugin to tag Issuarr's torrents.</summary>
public class Deluge(IHttpClientFactory http, TorrentFetcher fetcher) : DownloadClientBase(http)
{
    private static readonly ConcurrentDictionary<string, string> Sessions = new();
    private int _id;

    public override string Implementation => "Deluge";
    public override string DisplayName => "Deluge";
    public override string Description => "Torrent · tags downloads with a label (enable the Label plugin in Deluge)";
    public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
    protected override int DefaultPort => 8112;
    protected override bool HasUsername => false;
    protected override string CategoryLabel => "Label";
    protected override IEnumerable<ProviderField> ExtraAuthFields => [new("password", "Password", FieldType.Password, "Deluge Web UI password", "deluge")];

    public override async Task<string> Add(ProviderDefinition def, ReleaseInfo release, CancellationToken ct)
    {
        var torrent = await fetcher.Fetch(release, ct);
        var result = torrent.Magnet != null
            ? await Call(def, "core.add_torrent_magnet", [torrent.Magnet, new JsonObject()], ct)
            : await Call(def, "core.add_torrent_file", ["release.torrent", Convert.ToBase64String(torrent.TorrentFile!), new JsonObject()], ct);
        var hash = ((string?)result ?? torrent.Hash).ToLowerInvariant();

        var label = Category(def).ToLowerInvariant();
        try
        {
            var labels = await Call(def, "label.get_labels", [], ct);
            if (labels is JsonArray arr && arr.All(l => (string?)l != label))
            {
                await Call(def, "label.add", [label], ct);
            }

            await Call(def, "label.set_torrent", [hash, label], ct);
        }
        catch (ProviderTestException)
        {
            throw new ProviderTestException("Torrent added, but Deluge's Label plugin isn't enabled so Issuarr can't track it. Enable Label in Deluge → Preferences → Plugins.");
        }

        return hash;
    }

    public override async Task<List<DownloadItem>> GetItems(ProviderDefinition def, CancellationToken ct)
    {
        var fields = new JsonArray("name", "total_size", "total_done", "eta", "state", "save_path", "message", "progress", "label");
        var result = await Call(def, "core.get_torrents_status", [new JsonObject { ["label"] = Category(def).ToLowerInvariant() }, fields], ct);
        var items = new List<DownloadItem>();
        foreach (var (hash, t) in result?.AsObject() ?? [])
        {
            var total = (long?)t!["total_size"] ?? 0;
            var done = (long?)t["total_done"] ?? 0;
            var state = (string?)t["state"] ?? "";
            var eta = (long?)t["eta"] ?? 0;
            var progress = (double?)t["progress"] ?? 0;
            var name = (string?)t["name"] ?? "";

            items.Add(new DownloadItem
            {
                DownloadId = hash.ToLowerInvariant(),
                Title = name,
                TotalSize = total,
                RemainingSize = Math.Max(0, total - done),
                Eta = eta > 0 ? TimeSpan.FromSeconds(eta) : null,
                Status = state switch
                {
                    "Error" => DownloadItemStatus.Warning,
                    "Paused" when progress >= 100 => DownloadItemStatus.Completed,
                    "Paused" => DownloadItemStatus.Paused,
                    "Queued" or "Checking" or "Allocating" => DownloadItemStatus.Queued,
                    "Seeding" => DownloadItemStatus.Completed,
                    _ when progress >= 100 => DownloadItemStatus.Completed,
                    _ => DownloadItemStatus.Downloading,
                },
                Message = state == "Error" ? (string?)t["message"] : null,
                OutputPath = MapPath(def, $"{((string?)t["save_path"] ?? "").TrimEnd('/')}/{name}"),
            });
        }

        return items;
    }

    public override Task Remove(ProviderDefinition def, string downloadId, bool deleteData, CancellationToken ct) =>
        Call(def, "core.remove_torrent", [downloadId, deleteData], ct);

    public override async Task Test(ProviderDefinition def, CancellationToken ct)
    {
        Sessions.TryRemove(Key(def), out _);
        await Call(def, "core.get_torrents_status", [new JsonObject(), new JsonArray("name")], ct);
    }

    private static string Key(ProviderDefinition def) => $"{def.Id}|{def.Get("host")}|{def.Get("port")}";

    private async Task<JsonNode?> Call(ProviderDefinition def, string method, JsonNode?[] args, CancellationToken ct, bool retry = true)
    {
        var key = Key(def);
        if (!Sessions.TryGetValue(key, out var cookie))
        {
            cookie = await Login(def, ct);
            Sessions[key] = cookie;
        }

        var (result, error) = await Raw(def, cookie, method, args, ct);
        if (error != null && retry && ((int?)error["code"] == 1 || ((string?)error["message"])?.Contains("Not authenticated") == true))
        {
            Sessions.TryRemove(key, out _);
            return await Call(def, method, args, ct, retry: false);
        }

        if (error != null)
        {
            throw new ProviderTestException($"Deluge: {(string?)error["message"]}");
        }

        return result;
    }

    private async Task<string> Login(ProviderDefinition def, CancellationToken ct)
    {
        var (result, _, cookie) = await RawWithCookie(def, "", "auth.login", [def.Get("password", "deluge")], ct);
        if (result?.GetValue<bool>() != true)
        {
            throw new ProviderTestException("Deluge login failed. Check the password.");
        }

        // The Web UI must be connected to a Deluge daemon.
        var (connected, _) = await Raw(def, cookie, "web.connected", [], ct);
        if (connected?.GetValue<bool>() != true)
        {
            var (hosts, _) = await Raw(def, cookie, "web.get_hosts", [], ct);
            var hostId = (string?)hosts?.AsArray().FirstOrDefault()?[0];
            if (hostId == null)
            {
                throw new ProviderTestException("Deluge Web UI isn't connected to a daemon. Open the Web UI and connect it.");
            }

            await Raw(def, cookie, "web.connect", [hostId], ct);
        }

        return cookie;
    }

    private async Task<(JsonNode? Result, JsonNode? Error)> Raw(ProviderDefinition def, string cookie, string method, JsonNode?[] args, CancellationToken ct)
    {
        var (r, e, _) = await RawWithCookie(def, cookie, method, args, ct);
        return (r, e);
    }

    private async Task<(JsonNode? Result, JsonNode? Error, string Cookie)> RawWithCookie(ProviderDefinition def, string cookie, string method, JsonNode?[] args, CancellationToken ct)
    {
        var payload = new JsonObject { ["method"] = method, ["params"] = new JsonArray(args.Select(a => a?.DeepClone()).ToArray()), ["id"] = Interlocked.Increment(ref _id) };
        using var req = new HttpRequestMessage(HttpMethod.Post, Url(def, "json")) { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") };
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
            throw Unreachable("Deluge", e);
        }

        using (res)
        {
            var body = JsonNode.Parse(await Http.ReadOrThrow(res, "Deluge", ct))!;
            var newCookie = res.Headers.TryGetValues("Set-Cookie", out var values) ? values.First().Split(';')[0] : cookie;
            return (body["result"], body["error"], newCookie);
        }
    }
}
