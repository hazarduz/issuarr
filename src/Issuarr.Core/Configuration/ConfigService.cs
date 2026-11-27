using Dapper;
using Issuarr.Core.Datastore;

namespace Issuarr.Core.Configuration;

/// <summary>
/// App settings stored in the database (everything that isn't needed before the
/// database is open — those live in config.xml). Defaults apply until changed.
/// </summary>
public interface IConfigService
{
    IReadOnlyDictionary<string, string> All();
    void SaveMany(IDictionary<string, string> values);

    bool RenameIssues { get; }
    string ComicIssueFormat { get; }
    string MagazineIssueFormat { get; }
    string TitleFolderFormat { get; }
    bool UseHardlinks { get; }
    bool WriteComicInfo { get; }
    bool DeleteEmptyFolders { get; }
    string MetadataSource { get; }
    string ComicVineApiKey { get; }
    string MetronUsername { get; }
    string MetronPassword { get; }
    string MagazineMetadataSource { get; }
    string BookMetadataSource { get; }
    string GoogleBooksApiKey { get; }
    string BookFileFormat { get; }
    string AuthorFolderFormat { get; }
    bool KoSyncEnabled { get; }
    string KoSyncUsername { get; }

    /// <summary>MD5 of the sync password, which is what KOReader sends as its key.</summary>
    string KoSyncKey { get; }
    int RssSyncInterval { get; }
    int MinimumSeeders { get; }
    bool AutoRedownloadFailed { get; }
    bool RemoveCompletedDownloads { get; }
}

public class ConfigService(IDatabase db) : IConfigService
{
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["renameIssues"] = "true",
        ["comicIssueFormat"] = "{Title} {Issue:000} ({Release Year})",
        ["magazineIssueFormat"] = "{Title} - {Release Date:yyyy-MM}",
        ["titleFolderFormat"] = "{Title} ({Title Year})",
        ["useHardlinks"] = "true",
        ["writeComicInfo"] = "true",
        ["deleteEmptyFolders"] = "false",
        ["metadataSource"] = "comicvine",
        ["comicVineApiKey"] = "",
        ["metronUsername"] = "",
        ["metronPassword"] = "",
        ["magazineMetadataSource"] = "wikidata",
        ["bookMetadataSource"] = "openlibrary",
        ["googleBooksApiKey"] = "",
        ["bookFileFormat"] = "{Title} - {Issue} ({Release Year})",
        ["authorFolderFormat"] = "{Title}",
        ["kosyncEnabled"] = "false",
        ["kosyncUsername"] = "",
        ["kosyncKey"] = "",
        ["rssSyncInterval"] = "15",
        ["minimumSeeders"] = "1",
        ["autoRedownloadFailed"] = "true",
        ["removeCompletedDownloads"] = "false",
    };

    private Dictionary<string, string>? _cache;
    private readonly object _lock = new();

    public IReadOnlyDictionary<string, string> All()
    {
        lock (_lock)
        {
            if (_cache == null)
            {
                using var conn = db.Open();
                var stored = conn.Query<(string Key, string? Value)>("SELECT Key, Value FROM Config")
                    .ToDictionary(x => x.Key, x => x.Value ?? "");
                _cache = Defaults.ToDictionary(kv => kv.Key, kv => stored.TryGetValue(kv.Key, out var v) ? v : kv.Value);
            }

            return new Dictionary<string, string>(_cache);
        }
    }

    public void SaveMany(IDictionary<string, string> values)
    {
        var known = values.Where(kv => Defaults.ContainsKey(kv.Key)).ToList();
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        foreach (var (key, value) in known)
        {
            conn.Execute("INSERT INTO Config (Key, Value) VALUES (@key, @value) ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value",
                new { key, value }, tx);
        }

        tx.Commit();
        lock (_lock)
        {
            _cache = null;
        }
    }

    private string Get(string key) => All()[key];
    private bool GetBool(string key) => bool.TryParse(Get(key), out var b) && b;
    private int GetInt(string key) => int.TryParse(Get(key), out var i) ? i : int.Parse(Defaults[key]);

    public bool RenameIssues => GetBool("renameIssues");
    public string ComicIssueFormat => Get("comicIssueFormat");
    public string MagazineIssueFormat => Get("magazineIssueFormat");
    public string TitleFolderFormat => Get("titleFolderFormat");
    public bool UseHardlinks => GetBool("useHardlinks");
    public bool WriteComicInfo => GetBool("writeComicInfo");
    public bool DeleteEmptyFolders => GetBool("deleteEmptyFolders");
    public string MetadataSource => Get("metadataSource");
    public string ComicVineApiKey => Get("comicVineApiKey");
    public string MetronUsername => Get("metronUsername");
    public string MetronPassword => Get("metronPassword");
    public string MagazineMetadataSource => Get("magazineMetadataSource");
    public string BookMetadataSource => Get("bookMetadataSource");
    public string GoogleBooksApiKey => Get("googleBooksApiKey");
    public string BookFileFormat => Get("bookFileFormat");
    public string AuthorFolderFormat => Get("authorFolderFormat");
    public bool KoSyncEnabled => GetBool("kosyncEnabled");
    public string KoSyncUsername => Get("kosyncUsername");
    public string KoSyncKey => Get("kosyncKey");
    public int RssSyncInterval => Math.Max(10, GetInt("rssSyncInterval"));
    public int MinimumSeeders => GetInt("minimumSeeders");
    public bool AutoRedownloadFailed => GetBool("autoRedownloadFailed");
    public bool RemoveCompletedDownloads => GetBool("removeCompletedDownloads");
}
