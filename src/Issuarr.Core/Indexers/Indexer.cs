using Issuarr.Core.Providers;

namespace Issuarr.Core.Indexers;

public enum DownloadProtocol
{
    Unknown = 0,
    Usenet = 1,
    Torrent = 2,
}

public class ReleaseInfo
{
    public string Guid { get; set; } = "";
    public string Title { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string? InfoUrl { get; set; }
    public long Size { get; set; }
    public DateTime? PublishDate { get; set; }
    public string Indexer { get; set; } = "";
    public int IndexerId { get; set; }
    public int IndexerPriority { get; set; } = 25;
    public DownloadProtocol Protocol { get; set; }
    public int? Seeders { get; set; }
    public int? Peers { get; set; }
    public string? InfoHash { get; set; }
}

public interface IIndexer : IProvider
{
    DownloadProtocol Protocol { get; }
    Task<List<ReleaseInfo>> Search(ProviderDefinition def, string query, CancellationToken ct);
    Task<List<ReleaseInfo>> Recent(ProviderDefinition def, CancellationToken ct);
    Task Test(ProviderDefinition def, CancellationToken ct);
}

/// <summary>Newznab categories for comics and magazines (Books → Comics / Mags).</summary>
public static class Categories
{
    public const string Default = "7030,7010";
}
