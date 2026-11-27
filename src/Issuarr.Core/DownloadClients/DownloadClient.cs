using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Issuarr.Core.Indexers;
using Issuarr.Core.Providers;

namespace Issuarr.Core.DownloadClients;

public enum DownloadItemStatus
{
    Queued,
    Downloading,
    Paused,
    Completed,
    Failed,
    Warning,
}

public class DownloadItem
{
    public string DownloadId { get; set; } = "";
    public string Title { get; set; } = "";
    public long TotalSize { get; set; }
    public long RemainingSize { get; set; }
    public TimeSpan? Eta { get; set; }
    public DownloadItemStatus Status { get; set; }
    public string? Message { get; set; }

    /// <summary>Where the finished files are, already translated to a path Issuarr can see.</summary>
    public string? OutputPath { get; set; }

    public bool CanBeRemoved { get; set; } = true;
}

public interface IDownloadClient : IProvider
{
    DownloadProtocol Protocol { get; }

    /// <summary>Sends the release to the client and returns its id (torrent hash or nzo id).</summary>
    Task<string> Add(ProviderDefinition def, ReleaseInfo release, CancellationToken ct);

    Task<List<DownloadItem>> GetItems(ProviderDefinition def, CancellationToken ct);
    Task Remove(ProviderDefinition def, string downloadId, bool deleteData, CancellationToken ct);
    Task Test(ProviderDefinition def, CancellationToken ct);
}

/// <summary>Shared settings and helpers for every client.</summary>
public abstract class DownloadClientBase(IHttpClientFactory http) : IDownloadClient
{
    protected IHttpClientFactory HttpFactory { get; } = http;

    public abstract string Implementation { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public abstract DownloadProtocol Protocol { get; }
    protected abstract int DefaultPort { get; }
    protected virtual string DefaultUrlBase => "";
    protected virtual bool HasUsername => true;
    protected virtual string CategoryLabel => "Category";

    public virtual IReadOnlyList<ProviderField> Fields
    {
        get
        {
            var fields = new List<ProviderField>
            {
                new("host", "Host", HelpText: "Hostname or IP, e.g. qbittorrent or 192.168.1.20", Default: "localhost", Required: true),
                new("port", "Port", FieldType.Number, Default: DefaultPort.ToString()),
                new("useSsl", "Use SSL", FieldType.Checkbox, Default: "false"),
                new("urlBase", "URL Base", HelpText: "Only if the client is behind a reverse proxy sub-path", Default: DefaultUrlBase, Advanced: true),
            };
            if (HasUsername)
            {
                fields.Add(new("username", "Username"));
            }

            fields.AddRange(ExtraAuthFields);
            fields.Add(new("category", CategoryLabel, HelpText: "Keeps Issuarr's downloads separate from everything else", Default: "issuarr"));
            fields.Add(new("remotePath", "Remote Path", HelpText: "Remote path mapping: the download folder as the client sees it, e.g. /downloads", Advanced: true));
            fields.Add(new("localPath", "Local Path", HelpText: "…and the same folder as Issuarr sees it, e.g. /data/torrents", Advanced: true));
            return fields;
        }
    }

    protected virtual IEnumerable<ProviderField> ExtraAuthFields => [new("password", "Password", FieldType.Password)];

    public abstract Task<string> Add(ProviderDefinition def, ReleaseInfo release, CancellationToken ct);
    public abstract Task<List<DownloadItem>> GetItems(ProviderDefinition def, CancellationToken ct);
    public abstract Task Remove(ProviderDefinition def, string downloadId, bool deleteData, CancellationToken ct);
    public abstract Task Test(ProviderDefinition def, CancellationToken ct);

    protected static string BaseUrl(ProviderDefinition def, int defaultPort, string defaultUrlBase = "")
    {
        var scheme = def.GetBool("useSsl") ? "https" : "http";
        var urlBase = def.Get("urlBase", defaultUrlBase).Trim('/');
        return $"{scheme}://{def.Get("host", "localhost")}:{def.GetInt("port", defaultPort)}" + (urlBase.Length > 0 ? "/" + urlBase : "");
    }

    protected string Url(ProviderDefinition def, string path) => Http.Combine(BaseUrl(def, DefaultPort, DefaultUrlBase), path);

    protected static string Category(ProviderDefinition def) => def.Get("category", "issuarr");

    /// <summary>Applies the remote path mapping so paths reported by the client work for Issuarr.</summary>
    protected static string? MapPath(ProviderDefinition def, string? remote)
    {
        if (string.IsNullOrEmpty(remote))
        {
            return remote;
        }

        var from = def.Get("remotePath").TrimEnd('/', '\\');
        var to = def.Get("localPath").TrimEnd('/', '\\');
        if (from.Length > 0 && to.Length > 0 && (remote.Equals(from, StringComparison.Ordinal) || remote.StartsWith(from + "/", StringComparison.Ordinal) || remote.StartsWith(from + "\\", StringComparison.Ordinal)))
        {
            return to + remote[from.Length..].Replace('\\', '/');
        }

        return remote;
    }

    protected static ProviderTestException Unreachable(string client, Exception e) =>
        new($"Can't connect to {client}: {e.Message}");
}

/// <summary>Fetches .torrent files and works out their info hash.</summary>
public class TorrentFetcher(IHttpClientFactory http)
{
    public const string NoRedirectClient = "issuarr-noredirect";

    public record Fetched(string? Magnet, byte[]? TorrentFile, string Hash);

    public async Task<Fetched> Fetch(ReleaseInfo release, CancellationToken ct)
    {
        if (release.DownloadUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            return new Fetched(release.DownloadUrl, null, MagnetHash(release.DownloadUrl) ?? throw new InvalidOperationException("Magnet link has no info hash"));
        }

        // Indexers (and Prowlarr) sometimes redirect a download link to a magnet, which HttpClient
        // can't follow, so redirects are handled by hand.
        var url = release.DownloadUrl;
        var client = http.CreateClient(NoRedirectClient);
        for (var hop = 0; hop < 5; hop++)
        {
            using var res = await client.GetAsync(url, ct);
            if ((int)res.StatusCode is >= 300 and < 400 && res.Headers.Location is { } loc)
            {
                var next = loc.IsAbsoluteUri ? loc.ToString() : new Uri(new Uri(url), loc).ToString();
                if (next.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    return new Fetched(next, null, MagnetHash(next) ?? throw new InvalidOperationException("Magnet link has no info hash"));
                }

                url = next;
                continue;
            }

            if (res.StatusCode != HttpStatusCode.OK)
            {
                throw new InvalidOperationException($"Indexer returned HTTP {(int)res.StatusCode} for the torrent file");
            }

            var bytes = await res.Content.ReadAsByteArrayAsync(ct);
            return new Fetched(null, bytes, InfoHash(bytes));
        }

        throw new InvalidOperationException("Too many redirects fetching the torrent file");
    }

    public static string? MagnetHash(string magnet)
    {
        var m = Regex.Match(magnet, @"xt=urn:btih:([a-zA-Z0-9]+)", RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            return null;
        }

        var hash = m.Groups[1].Value;
        return hash.Length == 32 ? Convert.ToHexString(Base32Decode(hash)).ToLowerInvariant() : hash.ToLowerInvariant();
    }

    /// <summary>SHA-1 of the bencoded "info" dictionary.</summary>
    public static string InfoHash(byte[] torrent)
    {
        var i = 0;
        if (torrent.Length == 0 || torrent[0] != 'd')
        {
            throw new InvalidOperationException("Not a valid .torrent file");
        }

        i++;
        while (i < torrent.Length && torrent[i] != 'e')
        {
            var key = ReadString(torrent, ref i);
            var start = i;
            Skip(torrent, ref i);
            if (key == "info")
            {
                return Convert.ToHexString(SHA1.HashData(torrent.AsSpan(start, i - start))).ToLowerInvariant();
            }
        }

        throw new InvalidOperationException("Torrent file has no info dictionary");
    }

    private static string ReadString(byte[] b, ref int i)
    {
        var colon = Array.IndexOf(b, (byte)':', i);
        var len = int.Parse(Encoding.ASCII.GetString(b, i, colon - i));
        var s = Encoding.UTF8.GetString(b, colon + 1, len);
        i = colon + 1 + len;
        return s;
    }

    private static void Skip(byte[] b, ref int i)
    {
        switch ((char)b[i])
        {
            case 'i':
                i = Array.IndexOf(b, (byte)'e', i) + 1;
                break;
            case 'l':
            case 'd':
                i++;
                while (b[i] != 'e')
                {
                    Skip(b, ref i);
                }

                i++;
                break;
            default:
                ReadString(b, ref i);
                break;
        }
    }

    private static byte[] Base32Decode(string s)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in s.ToUpperInvariant())
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return output.ToArray();
    }
}
