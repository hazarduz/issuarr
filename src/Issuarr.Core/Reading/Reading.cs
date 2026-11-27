using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Dapper;
using Issuarr.Core.Datastore;
using SharpCompress.Archives;

namespace Issuarr.Core.Reading;

public class ReadProgress
{
    public int UserId { get; set; } = Users.User.OwnerId;
    public int IssueId { get; set; }

    /// <summary>Page number for comics/PDFs, an EPUB CFI for books.</summary>
    public string Position { get; set; } = "";

    public double Percent { get; set; }
    public string Updated { get; set; } = "";
}

/// <summary>Each user's place in each issue/book.</summary>
public interface IReadProgressRepository
{
    ReadProgress? Get(int userId, int issueId);
    List<ReadProgress> All(int userId);
    Dictionary<int, double> PercentByIssue(int userId, IEnumerable<int> issueIds);
    void Save(ReadProgress progress);
    void Clear(int userId, int issueId);
}

public class ReadProgressRepository(IDatabase db, IClock clock) : IReadProgressRepository
{
    public ReadProgress? Get(int userId, int issueId)
    {
        using var conn = db.Open();
        return conn.QuerySingleOrDefault<ReadProgress>("SELECT * FROM ReadProgress WHERE UserId = @userId AND IssueId = @issueId", new { userId, issueId });
    }

    public List<ReadProgress> All(int userId)
    {
        using var conn = db.Open();
        return conn.Query<ReadProgress>("SELECT * FROM ReadProgress WHERE UserId = @userId ORDER BY Updated DESC", new { userId }).AsList();
    }

    public Dictionary<int, double> PercentByIssue(int userId, IEnumerable<int> issueIds)
    {
        using var conn = db.Open();
        return conn.Query<(int IssueId, double Percent)>("SELECT IssueId, Percent FROM ReadProgress WHERE UserId = @userId AND IssueId IN @issueIds", new { userId, issueIds })
            .ToDictionary(x => x.IssueId, x => x.Percent);
    }

    public void Save(ReadProgress p)
    {
        p.Updated = clock.UtcNow.ToString("O");
        p.Percent = Math.Clamp(p.Percent, 0, 100);
        using var conn = db.Open();
        conn.Execute(
            """
            INSERT INTO ReadProgress (UserId, IssueId, Position, Percent, Updated) VALUES (@UserId, @IssueId, @Position, @Percent, @Updated)
            ON CONFLICT(UserId, IssueId) DO UPDATE SET Position = excluded.Position, Percent = excluded.Percent, Updated = excluded.Updated
            """,
            p);
    }

    public void Clear(int userId, int issueId)
    {
        using var conn = db.Open();
        conn.Execute("DELETE FROM ReadProgress WHERE UserId = @userId AND IssueId = @issueId", new { userId, issueId });
    }
}

/// <summary>
/// Reads pages out of comic archives (CBZ = zip, CBR = rar, CB7 = 7z) without extracting
/// them to disk. Page lists are cached per file so turning pages only reads one entry.
/// </summary>
public static partial class ComicArchive
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".bmp", ".jxl" };
    private static readonly ConcurrentDictionary<string, (DateTime Modified, List<string> Pages)> Cache = new();

    public static List<string> Pages(string path)
    {
        var modified = File.GetLastWriteTimeUtc(path);
        if (Cache.TryGetValue(path, out var cached) && cached.Modified == modified)
        {
            return cached.Pages;
        }

        using var archive = ArchiveFactory.OpenArchive(path);
        var pages = archive.Entries
            .Where(e => !e.IsDirectory && e.Key != null && ImageExtensions.Contains(Path.GetExtension(e.Key))
                        && !e.Key.Split('/', '\\').Any(part => part.StartsWith('.') || part == "__MACOSX"))
            .Select(e => e.Key!)
            .OrderBy(k => k, NaturalComparer.Instance)
            .ToList();

        if (Cache.Count > 200)
        {
            Cache.Clear();
        }

        Cache[path] = (modified, pages);
        return pages;
    }

    /// <summary>Returns the page's bytes and content type, or null if the page doesn't exist.</summary>
    public static (byte[] Data, string ContentType)? Page(string path, int index)
    {
        var pages = Pages(path);
        if (index < 0 || index >= pages.Count)
        {
            return null;
        }

        using var archive = ArchiveFactory.OpenArchive(path);
        var entry = archive.Entries.First(e => e.Key == pages[index]);
        using var stream = entry.OpenEntryStream();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return (ms.ToArray(), ContentType(pages[index]));
    }

    public static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".avif" => "image/avif",
        ".bmp" => "image/bmp",
        ".jxl" => "image/jxl",
        _ => "image/jpeg",
    };

    /// <summary>"page2.jpg" before "page10.jpg".</summary>
    public partial class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var a = Digits().Split(x ?? "");
            var b = Digits().Split(y ?? "");
            for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                if (a[i] == b[i])
                {
                    continue;
                }

                if (long.TryParse(a[i], out var na) && long.TryParse(b[i], out var nb))
                {
                    var c = na.CompareTo(nb);
                    if (c != 0)
                    {
                        return c;
                    }

                    continue;
                }

                return string.Compare(a[i], b[i], StringComparison.OrdinalIgnoreCase);
            }

            return a.Length.CompareTo(b.Length);
        }

        [GeneratedRegex(@"(\d+)")]
        private static partial Regex Digits();
    }
}
