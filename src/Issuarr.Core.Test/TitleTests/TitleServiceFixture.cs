using Issuarr.Core.Datastore;
using Issuarr.Core.Demo;
using Issuarr.Core.Issues;
using Issuarr.Core.Titles;

namespace Issuarr.Core.Test.TitleTests;

internal class FixedClock(DateOnly today) : IClock
{
    public DateOnly Today { get; } = today;
    public DateTime UtcNow => Today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}

/// <summary>Runs against a real (temporary) SQLite database with migrations applied.</summary>
[TestFixture]
public class TitleServiceFixture
{
    private string _dbPath = "";
    private TitleService _titles = null!;
    private IssueRepository _issues = null!;
    private IssueFileRepository _files = null!;
    private readonly FixedClock _clock = new(new DateOnly(2026, 11, 26));

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"issuarr-test-{Guid.NewGuid():N}.db");
        var db = new Database($"Data Source={_dbPath};Foreign Keys=True");
        DatabaseMigrator.Migrate(db.ConnectionString);
        _issues = new IssueRepository(db);
        _files = new IssueFileRepository(db);
        _titles = new TitleService(new TitleRepository(db), _issues, _clock);
    }

    [TearDown]
    public void TearDown()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Test]
    public void add_should_generate_slug_sortname_and_path()
    {
        var t = _titles.Add(new Title { Name = "The Kitchen Table", Kind = TitleKind.Magazine, Year = 2012, Tags = ["food"] }, "/data/magazines");

        Assert.Multiple(() =>
        {
            Assert.That(t.Id, Is.GreaterThan(0));
            Assert.That(t.Slug, Is.EqualTo("the-kitchen-table"));
            Assert.That(t.SortName, Is.EqualTo("kitchen table"));
            Assert.That(t.Path, Is.EqualTo("/data/magazines/The Kitchen Table (2012)"));
            Assert.That(_titles.Get(t.Id)!.Tags, Is.EqualTo(new[] { "food" }));
        });
    }

    [Test]
    public void duplicate_names_should_get_unique_slugs()
    {
        _titles.Add(new Title { Name = "Velocity", Year = 2016 }, "/data");
        var second = _titles.Add(new Title { Name = "Velocity", Year = 2020 }, "/data");

        Assert.That(second.Slug, Is.EqualTo("velocity-2"));
    }

    [Test]
    public void should_find_title_from_release_name()
    {
        _titles.Add(new Title { Name = "Circuit & Solder", Year = 2015 }, "/data");

        Assert.That(_titles.FindByReleaseTitle("Circuit and Solder")?.Name, Is.EqualTo("Circuit & Solder"));
    }

    [Test]
    public void unmonitoring_title_should_unmonitor_its_issues()
    {
        var t = _titles.Add(new Title { Name = "Iron Tide", Year = 2022 }, "/data");
        _issues.InsertMany([new Issue { TitleId = t.Id, Number = "#1", SortKey = 1, ReleaseDate = "2026-01-01" }]);

        t.Monitored = false;
        _titles.Update(t);

        Assert.That(_issues.GetByTitle(t.Id).Single().Monitored, Is.False);
    }

    [Test]
    public void deleting_title_should_cascade_to_issues()
    {
        var t = _titles.Add(new Title { Name = "Neon Alley", Year = 2025 }, "/data");
        _issues.InsertMany([new Issue { TitleId = t.Id, Number = "#1", SortKey = 1, ReleaseDate = "2026-01-01" }]);

        _titles.Delete(t.Id);

        Assert.That(_issues.All(), Is.Empty);
    }

    [Test]
    public void missing_should_only_include_aired_monitored_issues_without_files()
    {
        var t = _titles.Add(new Title { Name = "Neon Alley", Year = 2025 }, "/data");
        var file = _files.Insert(new IssueFile { TitleId = t.Id, RelativePath = "a.cbz", Size = 1, Format = "CBZ", DateAdded = "x" });
        _issues.InsertMany(
        [
            new Issue { TitleId = t.Id, Number = "#1", SortKey = 1, ReleaseDate = "2026-01-01", IssueFileId = file.Id },
            new Issue { TitleId = t.Id, Number = "#2", SortKey = 2, ReleaseDate = "2026-02-01" },
            new Issue { TitleId = t.Id, Number = "#3", SortKey = 3, ReleaseDate = "2026-03-01", Monitored = false },
            new Issue { TitleId = t.Id, Number = "#4", SortKey = 4, ReleaseDate = "2026-12-24" },
        ]);

        var missing = _issues.Missing("2026-11-26");

        Assert.That(missing.Select(i => i.Number), Is.EqualTo(new[] { "#2" }));
    }

    [Test]
    public void demo_seeder_should_only_seed_an_empty_library()
    {
        var seeder = new DemoDataSeeder(_titles, _issues, _files, _clock);

        Assert.That(seeder.SeedIfEmpty(), Is.True);
        Assert.That(_titles.All(), Has.Count.EqualTo(12));
        Assert.That(seeder.SeedIfEmpty(), Is.False);
    }
}

[TestFixture]
public class IssueStatusFixture
{
    private static readonly DateOnly Today = new(2026, 11, 26);

    [TestCase(1, "2026-01-01", true, true, false, IssueStatus.Downloaded)]
    [TestCase(null, "2026-01-01", true, true, true, IssueStatus.Downloading)]
    [TestCase(null, "2026-12-01", true, true, false, IssueStatus.Unaired)]
    [TestCase(null, "2026-01-01", true, true, false, IssueStatus.Missing)]
    [TestCase(null, "2026-01-01", false, true, false, IssueStatus.Unmonitored)]
    [TestCase(null, "2026-01-01", true, false, false, IssueStatus.Unmonitored)]
    public void should_derive_status(int? fileId, string release, bool issueMonitored, bool titleMonitored, bool inQueue, IssueStatus expected)
    {
        var issue = new Issue { IssueFileId = fileId, ReleaseDate = release, Monitored = issueMonitored };

        Assert.That(IssueStatusCalculator.Calculate(issue, titleMonitored, Today, inQueue), Is.EqualTo(expected));
    }

    [TestCase(TitleKind.Comic, "Monthly", 37, "2026-11-07", "#37")]
    [TestCase(TitleKind.Magazine, "Monthly", 1, "2026-11-07", "Nov 2026")]
    [TestCase(TitleKind.Magazine, "Weekly", 1, "2026-11-26", "2026-W48")]
    [TestCase(TitleKind.Magazine, "Quarterly", 1, "2026-08-01", "Q3 2026")]
    public void should_format_issue_numbers(TitleKind kind, string frequency, int seq, string date, string expected)
    {
        Assert.That(IssueNumbering.Format(kind, frequency, seq, DateOnly.Parse(date)), Is.EqualTo(expected));
    }
}
