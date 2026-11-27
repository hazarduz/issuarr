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
public class IssueManagementFixture
{
    private class Books : IMetadataProvider
    {
        public string Source => "openlibrary";
        public string DisplayName => "Open Library";
        public IReadOnlyList<TitleKind> Kinds => [TitleKind.Book];
        public bool IsConfigured => true;
        public Task<List<SeriesInfo>> Search(string q, CancellationToken ct) => Task.FromResult(new List<SeriesInfo>());
        public Task<SeriesInfo> GetSeries(string id, CancellationToken ct) => Task.FromResult(new SeriesInfo { Name = "Andy Weir" });

        public Task<List<IssueInfo>> GetIssues(string id, CancellationToken ct) => Task.FromResult(new List<IssueInfo>
        {
            new() { ExternalId = "W1", Name = "The Martian", CoverDate = "2011-01-01", CoverUrl = "http://c/1.jpg" },
            new() { ExternalId = "W2", Name = "Project Hail Mary", CoverDate = "2021-01-01" },
            new() { ExternalId = "W3", Name = "아르테미스", CoverDate = "2021-01-01" },
        });
    }

    private TempDb _db = null!;
    private TitleService _titles = null!;
    private IssueRepository _issues = null!;
    private IssueFileRepository _files = null!;
    private RefreshTitleService _refresh = null!;
    private IssueManagementService _manage = null!;
    private Title _author = null!;
    private string _dir = "";

    [SetUp]
    public async Task SetUp()
    {
        _db = new TempDb();
        _dir = Directory.CreateTempSubdirectory("issuarr-im-").FullName;
        var clock = new FixedClock(new DateOnly(2026, 11, 26));
        var repo = new TitleRepository(_db.Db);
        _issues = new IssueRepository(_db.Db);
        _files = new IssueFileRepository(_db.Db);
        _titles = new TitleService(repo, _issues, clock);
        IMetadataProvider[] providers = [new Books()];
        _refresh = new RefreshTitleService(repo, _issues, providers, clock, NullLogger<RefreshTitleService>.Instance);
        var mgmt = new TitleManagementService(_titles, repo, providers, _refresh, _issues, _files, new HistoryService(_db.Db, clock), new FileNameBuilder(new ConfigService(_db.Db)), clock);
        _manage = new IssueManagementService(_titles, _issues, _files, mgmt, providers);

        _author = _titles.Add(new Title { Name = "Andy Weir", Kind = TitleKind.Book, MetadataSource = "openlibrary", ExternalId = "A1", Path = _dir }, _dir);
        await _refresh.Refresh(_author.Id, CancellationToken.None);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        Directory.Delete(_dir, true);
    }

    /// <summary>What Library Import makes from "Project Hail Mary - Andy Weir.epub" before matching.</summary>
    private Issue ImportedDuplicate(string name = "Project Hail Mary - Andy Weir", string file = "Project Hail Mary - Andy Weir.epub")
    {
        File.WriteAllText(Path.Combine(_dir, file), "x");
        var f = _files.Insert(new IssueFile { TitleId = _author.Id, RelativePath = file, Format = "EPUB", Size = 1, DateAdded = "x" });
        _issues.InsertMany([new Issue { TitleId = _author.Id, Number = name, SortKey = 1, ReleaseDate = "2024-05-01", IssueFileId = f.Id }]);
        return _issues.GetByTitle(_author.Id).Single(i => i.Number == name);
    }

    [Test]
    public async Task matching_a_duplicate_merges_it_into_the_metadata_entry()
    {
        var dup = ImportedDuplicate();
        var candidates = await _manage.Candidates(_author.Id, CancellationToken.None);
        Assert.That(candidates.Single(c => c.ExternalId == "W2").LocalIssueId, Is.Not.Null, "Open Library entry already exists locally");

        var merged = await _manage.Match(dup.Id, "W2", CancellationToken.None);

        var all = _issues.GetByTitle(_author.Id);
        Assert.Multiple(() =>
        {
            Assert.That(all.Select(i => i.Number), Is.EquivalentTo(new[] { "The Martian", "Project Hail Mary", "아르테미스" }), "duplicate row gone");
            Assert.That(merged.Number, Is.EqualTo("Project Hail Mary"));
            Assert.That(merged.IssueFileId, Is.EqualTo(dup.IssueFileId), "file moved onto the metadata entry");
            Assert.That(merged.ReleaseDate, Is.EqualTo("2021-01-01"));
        });
    }

    [Test]
    public async Task matching_an_unknown_entry_relinks_in_place()
    {
        _issues.Delete(_issues.GetByTitle(_author.Id).Where(i => i.ExternalId == "W1").Select(i => i.Id));
        var dup = ImportedDuplicate("Martian", "The Martian.epub");

        var matched = await _manage.Match(dup.Id, "W1", CancellationToken.None);

        Assert.That(matched.Id, Is.EqualTo(dup.Id));
        Assert.That(matched.Number, Is.EqualTo("The Martian"));
        Assert.That(matched.ExternalId, Is.EqualTo("W1"));
        Assert.That(matched.CoverUrl, Is.EqualTo("http://c/1.jpg"));
    }

    [Test]
    public async Task cannot_merge_two_files()
    {
        var target = _issues.GetByTitle(_author.Id).Single(i => i.ExternalId == "W2");
        var f = _files.Insert(new IssueFile { TitleId = _author.Id, RelativePath = "a.epub", Format = "EPUB", Size = 1, DateAdded = "x" });
        _issues.SetFile(target.Id, f.Id);
        var dup = ImportedDuplicate();

        var e = Assert.ThrowsAsync<ArgumentException>(() => _manage.Match(dup.Id, "W2", CancellationToken.None));
        Assert.That(e!.Message, Does.Contain("already has a file"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task deleting_a_metadata_entry_hides_it_through_refreshes_until_restored()
    {
        var korean = _issues.GetByTitle(_author.Id).Single(i => i.ExternalId == "W3");

        _manage.Delete(korean.Id, deleteFile: false);
        await _refresh.Refresh(_author.Id, CancellationToken.None);

        Assert.That(_issues.GetByTitle(_author.Id).Select(i => i.Number), Does.Not.Contain("아르테미스"));
        Assert.That(_issues.CountIgnored(_author.Id), Is.EqualTo(1));

        _manage.RestoreHidden(_author.Id);
        Assert.That(_issues.GetByTitle(_author.Id).Select(i => i.Number), Does.Contain("아르테미스"));
    }

    [Test]
    public void deleting_an_imported_entry_with_its_file_removes_both()
    {
        var dup = ImportedDuplicate();

        _manage.Delete(dup.Id, deleteFile: true);

        Assert.That(File.Exists(Path.Combine(_dir, "Project Hail Mary - Andy Weir.epub")), Is.False);
        Assert.That(_issues.GetByTitleIncludingIgnored(_author.Id).Any(i => i.Id == dup.Id), Is.False, "no metadata link, so the row is removed");
        Assert.That(_files.GetByTitle(_author.Id), Is.Empty);
    }

    [Test]
    public void deleting_but_keeping_the_file_leaves_it_on_disk()
    {
        var dup = ImportedDuplicate();
        _manage.Delete(dup.Id, deleteFile: false);
        Assert.That(File.Exists(Path.Combine(_dir, "Project Hail Mary - Andy Weir.epub")), Is.True);
    }

    [Test]
    public void edit_should_rename_and_reject_duplicates()
    {
        var dup = ImportedDuplicate();

        var edited = _manage.Edit(dup.Id, "Project Hail Mary (signed)", null, "2021-05-04");
        Assert.That(edited.Number, Is.EqualTo("Project Hail Mary (signed)"));
        Assert.That(edited.SortKey, Is.EqualTo(20210504));

        var e = Assert.Throws<ArgumentException>(() => _manage.Edit(dup.Id, "project hail mary", null, "2021-05-04"));
        Assert.That(e!.Message, Does.Contain("Use Match"));
    }
}
