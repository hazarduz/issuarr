using Dapper;
using Issuarr.Core.Datastore;

namespace Issuarr.Core.Issues;

public interface IIssueRepository
{
    List<Issue> GetByTitle(int titleId);
    List<Issue> All();
    Issue? Get(int id);
    List<Issue> Between(string start, string end);
    List<Issue> Missing(string today);
    void InsertMany(IEnumerable<Issue> issues);
    void SetMonitored(IEnumerable<int> ids, bool monitored);
    void SetMonitoredByTitle(int titleId, bool monitored);
    void SetFile(int issueId, int? fileId);
}

public class IssueRepository(IDatabase db) : IIssueRepository
{
    public List<Issue> GetByTitle(int titleId)
    {
        using var conn = db.Open();
        return conn.Query<Issue>("SELECT * FROM Issues WHERE TitleId = @titleId ORDER BY SortKey DESC", new { titleId }).AsList();
    }

    public List<Issue> All()
    {
        using var conn = db.Open();
        return conn.Query<Issue>("SELECT * FROM Issues").AsList();
    }

    public Issue? Get(int id)
    {
        using var conn = db.Open();
        return conn.QuerySingleOrDefault<Issue>("SELECT * FROM Issues WHERE Id = @id", new { id });
    }

    public List<Issue> Between(string start, string end)
    {
        using var conn = db.Open();
        return conn.Query<Issue>(
            "SELECT * FROM Issues WHERE ReleaseDate >= @start AND ReleaseDate <= @end ORDER BY ReleaseDate",
            new { start, end }).AsList();
    }

    public List<Issue> Missing(string today)
    {
        using var conn = db.Open();
        return conn.Query<Issue>(
            """
            SELECT i.* FROM Issues i
            JOIN Titles t ON t.Id = i.TitleId
            WHERE t.Monitored = 1 AND i.Monitored = 1 AND i.IssueFileId IS NULL AND i.ReleaseDate <= @today
            ORDER BY i.ReleaseDate DESC
            """,
            new { today }).AsList();
    }

    public void InsertMany(IEnumerable<Issue> issues)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        conn.Execute(
            """
            INSERT INTO Issues (TitleId, Number, SortKey, IssueTitle, ReleaseDate, Monitored, IssueFileId)
            VALUES (@TitleId, @Number, @SortKey, @IssueTitle, @ReleaseDate, @Monitored, @IssueFileId)
            """,
            issues, tx);
        tx.Commit();
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
}

public interface IIssueFileRepository
{
    List<IssueFile> All();
    List<IssueFile> GetByTitle(int titleId);
    IssueFile Insert(IssueFile file);
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
}
