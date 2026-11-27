using Issuarr.Core.Configuration;
using Issuarr.Core.History;
using Issuarr.Core.Issues;
using Issuarr.Core.Metadata;
using Issuarr.Core.Organizer;
using Issuarr.Core.Test.TitleTests;
using Issuarr.Core.Titles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Issuarr.Core.Test.PipelineTests;

[TestFixture]
public class MatchFixture
{
    private class Cv : IMetadataProvider
    {
        public string Source => "comicvine";
        public string DisplayName => "ComicVine";
        public bool IsConfigured => true;
        public Task<List<SeriesInfo>> Search(string query, CancellationToken ct) => Task.FromResult(new List<SeriesInfo>());

        public Task<SeriesInfo> GetSeries(string externalId, CancellationToken ct) =>
            externalId == "404" ? throw new MetadataException("Not found")
                : Task.FromResult(new SeriesInfo { Name = "Iron Tide", Year = 2022, Publisher = "Gravel & Ink", CoverUrl = "http://img/cover.jpg" });

        public Task<List<IssueInfo>> GetIssues(string externalId, CancellationToken ct) => Task.FromResult(new List<IssueInfo>
        {
            new() { ExternalId = "a1", Number = "1", StoreDate = "2022-03-02", Name = "First Light" },
            new() { ExternalId = "a2", Number = "2", StoreDate = "2022-04-06" },
            new() { ExternalId = "a3", Number = "3", StoreDate = "2022-05-04" },
        });
    }

    private TempDb _db = null!;
    private TitleService _titles = null!;
    private IssueRepository _issues = null!;
    private TitleManagementService _mgmt = null!;
    private Title _imported = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new TempDb();
        var clock = new FixedClock(new DateOnly(2026, 11, 26));
        var titleRepo = new TitleRepository(_db.Db);
        _issues = new IssueRepository(_db.Db);
        var files = new IssueFileRepository(_db.Db);
        _titles = new TitleService(titleRepo, _issues, clock);
        var providers = new IMetadataProvider[] { new Cv() };
        var refresh = new RefreshTitleService(titleRepo, _issues, providers, clock, NullLogger<RefreshTitleService>.Instance);
        _mgmt = new TitleManagementService(_titles, titleRepo, providers, refresh, _issues, files, new HistoryService(_db.Db, clock),
            new FileNameBuilder(new ConfigService(_db.Db)), clock);

        // What Library Import produces: a title with guessed dates and files, no metadata link.
        _imported = _titles.Add(new Title { Name = "Iron Tide [scans]", Year = 2024, Kind = TitleKind.Comic, Path = "/lib/Iron Tide" }, "/lib");
        var f1 = files.Insert(new IssueFile { TitleId = _imported.Id, RelativePath = "Iron Tide 001.cbz", Format = "CBZ", Size = 1, DateAdded = "x" });
        var f2 = files.Insert(new IssueFile { TitleId = _imported.Id, RelativePath = "Iron Tide 002.cbz", Format = "CBZ", Size = 1, DateAdded = "x" });
        _issues.InsertMany(
        [
            new Issue { TitleId = _imported.Id, Number = "#1", SortKey = 1, ReleaseDate = "2024-01-01", IssueFileId = f1.Id },
            new Issue { TitleId = _imported.Id, Number = "#2", SortKey = 2, ReleaseDate = "2024-01-01", IssueFileId = f2.Id },
        ]);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task match_should_keep_files_and_fill_in_issue_list()
    {
        var title = await _mgmt.Match(_imported.Id, "comicvine", "4050", useMetadataName: true, CancellationToken.None);
        var issues = _issues.GetByTitle(title.Id).OrderBy(i => i.SortKey).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(title.Name, Is.EqualTo("Iron Tide"));
            Assert.That(title.Slug, Is.EqualTo(_imported.Slug), "URL stays the same");
            Assert.That(title.Year, Is.EqualTo(2022));
            Assert.That(title.Publisher, Is.EqualTo("Gravel & Ink"));
            Assert.That(title.CoverUrl, Is.EqualTo("http://img/cover.jpg"));
            Assert.That(issues.Select(i => i.Number), Is.EqualTo(new[] { "#1", "#2", "#3" }), "no duplicates; #3 added");
            Assert.That(issues[0].IssueFileId, Is.Not.Null, "file kept on #1");
            Assert.That(issues[1].IssueFileId, Is.Not.Null, "file kept on #2");
            Assert.That(issues[2].IssueFileId, Is.Null, "#3 is missing");
            Assert.That(issues[0].ReleaseDate, Is.EqualTo("2022-03-02"), "guessed date replaced with the real one");
            Assert.That(issues[0].IssueTitle, Is.EqualTo("First Light"));
        });
    }

    [Test]
    public async Task can_keep_own_name_and_unmatch()
    {
        var title = await _mgmt.Match(_imported.Id, "comicvine", "4050", useMetadataName: false, CancellationToken.None);
        Assert.That(title.Name, Is.EqualTo("Iron Tide [scans]"));

        _mgmt.Unmatch(title.Id);
        var after = _titles.Get(title.Id)!;
        Assert.That(after.MetadataSource, Is.Null);
        Assert.That(_issues.GetByTitle(title.Id), Has.Count.EqualTo(3), "issues stay when unmatched");
        Assert.That(_issues.GetByTitle(title.Id).All(i => i.ExternalId == null), Is.True);
    }

    [Test]
    public void bad_series_or_duplicate_should_fail_without_changing_the_title()
    {
        Assert.ThrowsAsync<MetadataException>(() => _mgmt.Match(_imported.Id, "comicvine", "404", true, CancellationToken.None));
        Assert.That(_titles.Get(_imported.Id)!.MetadataSource, Is.Null);

        var other = _titles.Add(new Title { Name = "Other", Year = 2020, MetadataSource = "comicvine", ExternalId = "4050" }, "/lib");
        var e = Assert.ThrowsAsync<ArgumentException>(() => _mgmt.Match(_imported.Id, "comicvine", "4050", true, CancellationToken.None));
        Assert.That(e!.Message, Does.Contain(other.Name));
    }
}
