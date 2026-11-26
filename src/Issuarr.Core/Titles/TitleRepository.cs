using Dapper;
using Issuarr.Core.Datastore;

namespace Issuarr.Core.Titles;

public interface ITitleRepository
{
    List<Title> All();
    Title? Get(int id);
    Title? FindBySlug(string slug);
    Title Insert(Title title);
    void Update(Title title);
    void Delete(int id);
}

public class TitleRepository(IDatabase db) : ITitleRepository
{
    private const string Columns =
        "Name, SortName, Slug, Kind, Publisher, Year, Frequency, Status, Monitored, QualityProfile, Path, Overview, Tags, Palette, Added";

    public List<Title> All()
    {
        using var conn = db.Open();
        return conn.Query<Title>("SELECT * FROM Titles ORDER BY SortName").AsList();
    }

    public Title? Get(int id)
    {
        using var conn = db.Open();
        return conn.QuerySingleOrDefault<Title>("SELECT * FROM Titles WHERE Id = @id", new { id });
    }

    public Title? FindBySlug(string slug)
    {
        using var conn = db.Open();
        return conn.QuerySingleOrDefault<Title>("SELECT * FROM Titles WHERE Slug = @slug", new { slug });
    }

    public Title Insert(Title title)
    {
        using var conn = db.Open();
        var values = string.Join(", ", Columns.Split(", ").Select(c => "@" + c));
        title.Id = conn.ExecuteScalar<int>($"INSERT INTO Titles ({Columns}) VALUES ({values}); SELECT last_insert_rowid();", title);
        return title;
    }

    public void Update(Title title)
    {
        using var conn = db.Open();
        var set = string.Join(", ", Columns.Split(", ").Select(c => $"{c} = @{c}"));
        conn.Execute($"UPDATE Titles SET {set} WHERE Id = @Id", title);
    }

    public void Delete(int id)
    {
        using var conn = db.Open();
        conn.Execute("DELETE FROM Titles WHERE Id = @id", new { id });
    }
}
