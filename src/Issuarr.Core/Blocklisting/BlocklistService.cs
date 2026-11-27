using Dapper;
using Issuarr.Core.Datastore;
using Issuarr.Core.Indexers;

namespace Issuarr.Core.Blocklisting;

public class BlocklistItem
{
    public int Id { get; set; }
    public int TitleId { get; set; }
    public int? IssueId { get; set; }
    public string SourceTitle { get; set; } = "";
    public DownloadProtocol Protocol { get; set; }
    public string? Indexer { get; set; }
    public string? Message { get; set; }
    public string Date { get; set; } = "";
}

public interface IBlocklistService
{
    void Add(BlocklistItem item);
    List<BlocklistItem> All();
    bool IsBlocklisted(int titleId, string sourceTitle);
    void Remove(int id);
    void Clear();
}

public class BlocklistService(IDatabase db, IClock clock) : IBlocklistService
{
    public void Add(BlocklistItem item)
    {
        item.Date = clock.UtcNow.ToString("O");
        using var conn = db.Open();
        conn.Execute(
            """
            INSERT INTO Blocklist (TitleId, IssueId, SourceTitle, Protocol, Indexer, Message, Date)
            VALUES (@TitleId, @IssueId, @SourceTitle, @Protocol, @Indexer, @Message, @Date)
            """,
            item);
    }

    public List<BlocklistItem> All()
    {
        using var conn = db.Open();
        return conn.Query<BlocklistItem>("SELECT * FROM Blocklist ORDER BY Date DESC").AsList();
    }

    public bool IsBlocklisted(int titleId, string sourceTitle)
    {
        using var conn = db.Open();
        return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Blocklist WHERE TitleId = @titleId AND SourceTitle = @sourceTitle COLLATE NOCASE",
            new { titleId, sourceTitle }) > 0;
    }

    public void Remove(int id)
    {
        using var conn = db.Open();
        conn.Execute("DELETE FROM Blocklist WHERE Id = @id", new { id });
    }

    public void Clear()
    {
        using var conn = db.Open();
        conn.Execute("DELETE FROM Blocklist");
    }
}
