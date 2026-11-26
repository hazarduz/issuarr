using Issuarr.Core.Datastore;
using Issuarr.Core.Issues;
using Issuarr.Core.LibraryImport;
using Issuarr.Core.Test.TitleTests;
using Issuarr.Core.Titles;

namespace Issuarr.Core.Test.LibraryImportTests;

[TestFixture]
public class LibraryImportServiceFixture
{
    private string _dbPath = "";
    private string _root = "";
    private TitleService _titles = null!;
    private IssueRepository _issues = null!;
    private LibraryImportService _import = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"issuarr-test-{Guid.NewGuid():N}.db");
        _root = Directory.CreateTempSubdirectory("issuarr-import-").FullName;
        var db = new Database($"Data Source={_dbPath};Foreign Keys=True");
        DatabaseMigrator.Migrate(db.ConnectionString);
        var clock = new FixedClock(new DateOnly(2026, 11, 26));
        _issues = new IssueRepository(db);
        var files = new IssueFileRepository(db);
        _titles = new TitleService(new TitleRepository(db), _issues, clock);
        _import = new LibraryImportService(_titles, _issues, files, clock);

        Touch("Iron Tide (2022)/Iron Tide 001 (2022) (digital).cbz");
        Touch("Iron Tide (2022)/Iron Tide 002 (2022) (digital).cbz");
        Touch("Iron Tide (2022)/Iron Tide 002 (2022) (scan).cbr");   // duplicate issue
        Touch("Iron Tide (2022)/Extras/cover-gallery.cbz");           // no issue number
        Touch("Circuit & Solder/2026/Circuit & Solder - 2026-10.pdf");
        Touch("Circuit & Solder/2026/Circuit.and.Solder.November.2026.PDF");
        Touch("Empty Folder/readme.txt");
        Touch(".hidden/Secret 001.cbz");
    }

    [TearDown]
    public void TearDown()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        Directory.Delete(_root, true);
    }

    private void Touch(string rel)
    {
        var full = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[128]);
    }

    [Test]
    public void scan_should_find_one_candidate_per_folder_with_comic_files()
    {
        var found = _import.Scan(_root);

        Assert.That(found.Select(c => c.Name), Is.EqualTo(new[] { "Circuit & Solder", "Iron Tide" }));

        var iron = found.Single(c => c.Name == "Iron Tide");
        var circuit = found.Single(c => c.Name == "Circuit & Solder");
        Assert.Multiple(() =>
        {
            Assert.That(iron.Year, Is.EqualTo(2022));
            Assert.That(iron.Kind, Is.EqualTo(TitleKind.Comic));
            Assert.That(iron.FileCount, Is.EqualTo(4));
            Assert.That(iron.MatchedFileCount, Is.EqualTo(3));
            Assert.That(circuit.Kind, Is.EqualTo(TitleKind.Magazine));
            Assert.That(circuit.Year, Is.EqualTo(2026));
        });
    }

    [Test]
    public void scan_should_treat_chosen_folder_as_a_title_when_it_holds_files()
    {
        var found = _import.Scan(Path.Combine(_root, "Iron Tide (2022)"));

        Assert.That(found.First().Name, Is.EqualTo("Iron Tide"));
        Assert.That(found.First().Path, Is.EqualTo(Path.Combine(_root, "Iron Tide (2022)")));
    }

    [Test]
    public void import_should_create_title_issues_and_files()
    {
        var path = Path.Combine(_root, "Iron Tide (2022)");
        var result = _import.Import([new ImportRequest { Path = path, Name = "Iron Tide", Year = 2022, Kind = TitleKind.Comic }]).Single();

        Assert.That(result.Error, Is.Null);
        Assert.That(result.IssuesImported, Is.EqualTo(2));
        Assert.That(result.UnmatchedFiles, Has.Count.EqualTo(2));

        var title = _titles.Get(result.TitleId!.Value)!;
        var issues = _issues.GetByTitle(title.Id);
        Assert.Multiple(() =>
        {
            Assert.That(title.Path, Is.EqualTo(path));
            Assert.That(title.Palette, Has.Count.EqualTo(2));
            Assert.That(issues.Select(i => i.Number), Is.EqualTo(new[] { "#2", "#1" }));
            Assert.That(issues.All(i => i.IssueFileId != null), Is.True);
        });
    }

    [Test]
    public void import_should_build_dated_magazine_issues()
    {
        var result = _import.Import([new ImportRequest { Path = Path.Combine(_root, "Circuit & Solder"), Name = "Circuit & Solder", Kind = TitleKind.Magazine }]).Single();

        var title = _titles.Get(result.TitleId!.Value)!;
        Assert.That(title.Frequency, Is.EqualTo("Monthly"));
        Assert.That(title.Year, Is.EqualTo(2026));
        Assert.That(_issues.GetByTitle(title.Id).Select(i => i.Number), Is.EqualTo(new[] { "Nov 2026", "Oct 2026" }));
    }

    [Test]
    public void import_twice_should_fail_and_scan_should_flag_existing()
    {
        var req = new ImportRequest { Path = Path.Combine(_root, "Iron Tide (2022)"), Name = "Iron Tide", Kind = TitleKind.Comic };
        _import.Import([req]);

        Assert.That(_import.Import([req]).Single().Error, Does.Contain("already"));
        Assert.That(_import.Scan(_root).Single(c => c.Name == "Iron Tide").ExistingTitleId, Is.Not.Null);
    }

    [TestCase("Iron Tide (2022)", "Iron Tide", 2022)]
    [TestCase("Iron Tide [2022]", "Iron Tide", 2022)]
    [TestCase("2000 AD", "2000 AD", null)]
    [TestCase("Circuit_and_Solder", "Circuit and Solder", null)]
    public void should_parse_folder_names(string folder, string name, int? year)
    {
        Assert.That(LibraryImportService.ParseFolderName(folder), Is.EqualTo((name, year)));
    }
}
