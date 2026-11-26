using Dapper;
using Issuarr.Core.Datastore;

namespace Issuarr.Core.RootFolders;

public class RootFolder
{
    public int Id { get; set; }
    public string Path { get; set; } = "";
}

public interface IRootFolderService
{
    List<(RootFolder Folder, long? FreeSpace, bool Accessible)> All();
    RootFolder Add(string path);
    void Remove(int id);
}

public class RootFolderService(IDatabase db) : IRootFolderService
{
    public List<(RootFolder Folder, long? FreeSpace, bool Accessible)> All()
    {
        using var conn = db.Open();
        return conn.Query<RootFolder>("SELECT * FROM RootFolders ORDER BY Path")
            .Select(f => (f, FreeSpace(f.Path), Directory.Exists(f.Path)))
            .ToList();
    }

    public RootFolder Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathRooted(path))
        {
            throw new ArgumentException("Root folder must be an absolute path");
        }

        var normalized = path.Length > 1 ? path.TrimEnd('/', '\\') : path;
        if (!Directory.Exists(normalized))
        {
            throw new ArgumentException($"Folder does not exist: {normalized}");
        }

        using var conn = db.Open();
        if (conn.ExecuteScalar<int>("SELECT COUNT(*) FROM RootFolders WHERE Path = @normalized", new { normalized }) > 0)
        {
            throw new ArgumentException($"{normalized} is already a root folder");
        }

        var folder = new RootFolder { Path = normalized };
        folder.Id = conn.ExecuteScalar<int>("INSERT INTO RootFolders (Path) VALUES (@Path); SELECT last_insert_rowid();", folder);
        return folder;
    }

    public void Remove(int id)
    {
        using var conn = db.Open();
        conn.Execute("DELETE FROM RootFolders WHERE Id = @id", new { id });
    }

    private static long? FreeSpace(string path)
    {
        try
        {
            return Directory.Exists(path) ? new DriveInfo(path).AvailableFreeSpace : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
