using Dapper;
using Issuarr.Core.Datastore;

namespace Issuarr.Core.History;

public enum HistoryEventType
{
    Grabbed = 1,
    Imported = 2,
    DownloadFailed = 3,
    FileDeleted = 4,
    FileRenamed = 5,
    ImportFailed = 6,
}

public class HistoryItem
{
    public int Id { get; set; }
    public int TitleId { get; set; }
    public int? IssueId { get; set; }
    public HistoryEventType EventType { get; set; }
    public string SourceTitle { get; set; } = "";
    public string? DownloadId { get; set; }
    public string Date { get; set; } = "";
    public Dictionary<string, string> Data { get; set; } = new();
}

public interface IHistoryService
{
    HistoryItem Add(HistoryItem item);
    List<HistoryItem> Page(int page, int pageSize, int? titleId, out int total);
    List<HistoryItem> ForDownload(string downloadId);
    List<HistoryItem> ForTitle(int titleId);
    void Clear();
}

public class HistoryService(IDatabase db, IClock clock) : IHistoryService
{
    public HistoryItem Add(HistoryItem item)
    {
        if (string.IsNullOrEmpty(item.Date))
        {
            item.Date = clock.UtcNow.ToString("O");
        }

        using var conn = db.Open();
        item.Id = conn.ExecuteScalar<int>(
            """
            INSERT INTO History (TitleId, IssueId, EventType, SourceTitle, DownloadId, Date, Data)
            VALUES (@TitleId, @IssueId, @EventType, @SourceTitle, @DownloadId, @Date, @Data);
            SELECT last_insert_rowid();
            """,
            item);
        return item;
    }

    public List<HistoryItem> Page(int page, int pageSize, int? titleId, out int total)
    {
        using var conn = db.Open();
        var where = titleId.HasValue ? "WHERE TitleId = @titleId" : "";
        total = conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM History {where}", new { titleId });
        return conn.Query<HistoryItem>($"SELECT * FROM History {where} ORDER BY Date DESC, Id DESC LIMIT @pageSize OFFSET @offset",
            new { titleId, pageSize, offset = (Math.Max(page, 1) - 1) * pageSize }).AsList();
    }

    public List<HistoryItem> ForDownload(string downloadId)
    {
        using var conn = db.Open();
        return conn.Query<HistoryItem>("SELECT * FROM History WHERE DownloadId = @downloadId COLLATE NOCASE ORDER BY Date", new { downloadId }).AsList();
    }

    public List<HistoryItem> ForTitle(int titleId)
    {
        using var conn = db.Open();
        return conn.Query<HistoryItem>("SELECT * FROM History WHERE TitleId = @titleId ORDER BY Date DESC", new { titleId }).AsList();
    }

    public void Clear()
    {
        using var conn = db.Open();
        conn.Execute("DELETE FROM History");
    }
}
