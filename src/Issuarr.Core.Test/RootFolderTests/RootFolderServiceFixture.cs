using Issuarr.Core.Datastore;
using Issuarr.Core.RootFolders;

namespace Issuarr.Core.Test.RootFolderTests;

[TestFixture]
public class RootFolderServiceFixture
{
    private string _dbPath = "";
    private string _folder = "";
    private RootFolderService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"issuarr-test-{Guid.NewGuid():N}.db");
        _folder = Directory.CreateTempSubdirectory("issuarr-root-").FullName;
        var db = new Database($"Data Source={_dbPath}");
        DatabaseMigrator.Migrate(db.ConnectionString);
        _service = new RootFolderService(db);
    }

    [TearDown]
    public void TearDown()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        Directory.Delete(_folder, true);
    }

    [Test]
    public void should_add_and_remove_existing_folder()
    {
        var added = _service.Add(_folder + "/");

        Assert.That(added.Path, Is.EqualTo(_folder));
        Assert.That(_service.All().Single().Accessible, Is.True);

        _service.Remove(added.Id);
        Assert.That(_service.All(), Is.Empty);
    }

    [Test]
    public void should_reject_missing_folder() =>
        Assert.Throws<ArgumentException>(() => _service.Add(Path.Combine(_folder, "does-not-exist")));

    [Test]
    public void should_reject_relative_path() => Assert.Throws<ArgumentException>(() => _service.Add("data/comics"));

    [Test]
    public void should_reject_duplicate()
    {
        _service.Add(_folder);
        Assert.Throws<ArgumentException>(() => _service.Add(_folder));
    }
}
