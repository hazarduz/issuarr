using Dapper;
using Issuarr.Core.Datastore;

namespace Issuarr.Core.Issues;

public interface IIssueRepository
{
    List<Issue> GetByTitle(int titleId);

    /// <summary>Including hidden issues (for metadata refresh, so they aren't re-added).</summary>
    List<Issue> GetByTitleIncludingIgnored(int titleId);

    int CountIgnored(int titleId);
    void RestoreIgnored(int titleId);
    List<Issue> All();
    Issue? Get(int id);
    List<Issue> Get(IEnumerable<int> ids);
    List<Issue> Between(string start, string end);
    List<Issue> Missing(string today);
    List<Issue> WithFiles();
    void InsertMany(IEnumerable<Issue> issues);
    void Update(Issue issue);
    void Delete(IEnumerable<int> ids);
    void SetMonitored(IEnumerable<int> ids, bool monitored);
    void SetMonitoredByTitle(int titleId, bool monitored);
    void SetFile(int issueId, int? fileId);
    void ClearFile(int fileId);
}

public class IssueRepository(IDatabase db) : IIssueRepository
{
    public List<Issue> GetByTitle(int titleId)
    {
        using var conn = db.Open();
        return conn.Query<Issue>("SELECT * FROM Issues WHERE TitleId = @titleId AND Ignored = 0 ORDER BY SortKey DESC", new { titleId }).AsList();
    }

    public List<Issue> GetByTitleIncludingIgnored(int titleId)
    {
        using var conn = db.Open();
        return conn.Query<Issue>("SELECT * FROM Issues WHERE TitleId = @titleId ORDER BY SortKey DESC", new { titleId }).AsList();
    }

    public int CountIgnored(int titleId)
    {
        using var conn = db.Open();
        return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Issues WHERE TitleId = @titleId AND Ignored = 1", new { titleId });
    }

    public void RestoreIgnored(int titleId)
    {
        using var conn = db.Open();
        conn.Execute("UPDATE Issues SET Ignored = 0 WHERE TitleId = @titleId", new { titleId });
    }

    public List<Issue> All()
    {
        using var conn = db.Open();
        return conn.Query<Issue>("SELECT * FROM Issues WHERE Ignored = 0").AsList();
    }

    public Issue? Get(int id)
    {
        using var conn = db.Open();
        return conn.QuerySingleOrDefault<Issue>("SELECT * FROM Issues WHERE Id = @id", new { id });
    }

    public List<Issue> Get(IEnumerable<int> ids)
    {
        using var conn = db.Open();
        return conn.Query<Issue>("SELECT * FROM Issues WHERE Id IN @ids", new { ids }).AsList();
    }

    public List<Issue> Between(string start, string end)
    {
        using var conn = db.Open();
        return conn.Query<Issue>(
            "SELECT * FROM Issues WHERE Ignored = 0 AND ReleaseDate >= @start AND ReleaseDate <= @end ORDER BY ReleaseDate",
            new { start, end }).AsList();
    }

    public List<Issue> Missing(string today)
    {
        using var conn = db.Open();
        return conn.Query<Issue>(
            """
            SELECT i.* FROM Issues i
            JOIN Titles t ON t.Id = i.TitleId
            WHERE i.Ignored = 0 AND t.Monitored = 1 AND i.Monitored = 1 AND i.IssueFileId IS NULL AND i.ReleaseDate <= @today
            ORDER BY i.ReleaseDate DESC
            """,
            new { today }).AsList();
    }

    public List<Issue> WithFiles()
    {
        using var conn = db.Open();
        return conn.Query<Issue>("SELECT * FROM Issues WHERE IssueFileId IS NOT NULL AND Ignored = 0").AsList();
    }

    public void InsertMany(IEnumerable<Issue> issues)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        conn.Execute(
            """
            INSERT INTO Issues (TitleId, Number, SortKey, IssueTitle, ReleaseDate, Monitored, IssueFileId, ExternalId, CoverUrl, Ignored)
            VALUES (@TitleId, @Number, @SortKey, @IssueTitle, @ReleaseDate, @Monitored, @IssueFileId, @ExternalId, @CoverUrl, @Ignored)
            """,
            issues, tx);
        tx.Commit();
    }

    public void Update(Issue issue)
    {
        using var conn = db.Open();
        conn.Execute(
            """
            UPDATE Issues SET Number = @Number, SortKey = @SortKey, IssueTitle = @IssueTitle, ReleaseDate = @ReleaseDate,
                Monitored = @Monitored, IssueFileId = @IssueFileId, ExternalId = @ExternalId, CoverUrl = @CoverUrl, Ignored = @Ignored
            WHERE Id = @Id
            """,
            issue);
    }

    public void Delete(IEnumerable<int> ids)
    {
        using var conn = db.Open();
        conn.Execute("DELETE FROM Issues WHERE Id IN @ids", new { ids });
    }

    public void SetMonitored(IEnumerable<int> ids, bool monitored)
    {
        using var conn = db.Open();
        conn.Execute("UPDATE Issues SET Monitored = @monitored WHERE Id IN @ids", new { ids, monitored });
    }

    public void SetMonitoredByTitle(int titleId, bool monitored)
    {
        using var conn = db.Open();
        conn.Execute("UPDATE Issues SET Monitored = @monitored WHERE TitleId = @titleId", new { titleId, monitored });
    }

    public void SetFile(int issueId, int? fileId)
    {
        using var conn = db.Open();
        conn.Execute("UPDATE Issues SET IssueFileId = @fileId WHERE Id = @issueId", new { issueId, fileId });
    }

    public void ClearFile(int fileId)
    {
        using var conn = db.Open();
        conn.Execute("UPDATE Issues SET IssueFileId = NULL WHERE IssueFileId = @fileId", new { fileId });
    }
}

public interface IIssueFileRepository
{
    List<IssueFile> All();
    List<IssueFile> GetByTitle(int titleId);
    IssueFile? Get(int id);
    IssueFile Insert(IssueFile file);
    void UpdatePath(int id, string relativePath);
    void Delete(int id);
}

public class IssueFileRepository(IDatabase db) : IIssueFileRepository
{
    public List<IssueFile> All()
    {
        using var conn = db.Open();
        return conn.Query<IssueFile>("SELECT * FROM IssueFiles").AsList();
    }

    public List<IssueFile> GetByTitle(int titleId)
    {
        using var conn = db.Open();
        return conn.Query<IssueFile>("SELECT * FROM IssueFiles WHERE TitleId = @titleId", new { titleId }).AsList();
    }

    public IssueFile? Get(int id)
    {
        using var conn = db.Open();
        return conn.QuerySingleOrDefault<IssueFile>("SELECT * FROM IssueFiles WHERE Id = @id", new { id });
    }

    public IssueFile Insert(IssueFile file)
    {
        using var conn = db.Open();
        file.Id = conn.ExecuteScalar<int>(
            """
            INSERT INTO IssueFiles (TitleId, RelativePath, Size, Format, DateAdded)
            VALUES (@TitleId, @RelativePath, @Size, @Format, @DateAdded);
            SELECT last_insert_rowid();
            """,
            file);
        return file;
    }

    public void UpdatePath(int id, string relativePath)
    {
        using var conn = db.Open();
        conn.Execute("UPDATE IssueFiles SET RelativePath = @relativePath WHERE Id = @id", new { id, relativePath });
    }

    public void Delete(int id)
    {
        using var conn = db.Open();
        conn.Execute("DELETE FROM IssueFiles WHERE Id = @id", new { id });
    }
}
