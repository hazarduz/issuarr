using Issuarr.Core.Configuration;
using Issuarr.Core.History;
using Issuarr.Core.Issues;
using Issuarr.Core.MediaFiles;
using Issuarr.Core.Organizer;
using Issuarr.Core.Test.TitleTests;
using Issuarr.Core.Titles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Issuarr.Core.Test.PipelineTests;

[TestFixture]
public class DiskScanFixture
{
    private TempDb _db = null!;
    private string _root = "";
    private TitleService _titles = null!;
    private IssueRepository _issues = null!;
    private IssueFileRepository _files = null!;
    private DiskScanService _scan = null!;
    private TitleManagementService _mgmt = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new TempDb();
        _root = Directory.CreateTempSubdirectory("issuarr-scan-").FullName;
        var clock = new FixedClock(new DateOnly(2026, 11, 26));
        var repo = new TitleRepository(_db.Db);
        _issues = new IssueRepository(_db.Db);
        _files = new IssueFileRepository(_db.Db);
        _titles = new TitleService(repo, _issues, clock);
        var history = new HistoryService(_db.Db, clock);
        _scan = new DiskScanService(_titles, _issues, _files, history, clock, NullLogger<DiskScanService>.Instance);
        var refresh = new RefreshTitleService(repo, _issues, [], clock, NullLogger<RefreshTitleService>.Instance, _scan);
        _mgmt = new TitleManagementService(_titles, repo, [], refresh, _issues, _files, history, new FileNameBuilder(new ConfigService(_db.Db)), clock, _scan);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        Directory.Delete(_root, true);
    }

    private void Touch(string rel, int size = 10)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[size]);
    }

    [Test]
    public async Task adding_a_magazine_uses_the_existing_folder_and_picks_up_its_files()
    {
        Touch("Retro Gamer/Retro Gamer - November 2026.pdf");
        Touch("Retro Gamer/Retro Gamer 2026-10.pdf");
        Touch("Retro Gamer/2024/Retro Gamer - Issue 260 (2024).pdf");
        Touch("Retro Gamer/Retro_Gamer_UK_Issue_261_2024.pdf");
        Touch("Retro Gamer/notes.txt");

        var t = await _mgmt.Add(new Title { Name = "Retro Gamer", Kind = TitleKind.Magazine, Frequency = "Monthly", Year = 2026, QualityProfile = "PDF Preferred" },
            _root, new AddTitleOptions { FirstIssueDate = new DateOnly(2026, 9, 1) }, CancellationToken.None);

        Assert.That(t.Path, Is.EqualTo(Path.Combine(_root, "Retro Gamer")), "existing folder reused, not \"Retro Gamer (2026)\"");
        var issues = _issues.GetByTitle(t.Id);
        var withFiles = issues.Where(i => i.IssueFileId != null).Select(i => i.Number).ToList();
        Assert.That(withFiles, Is.EquivalentTo(new[] { "Nov 2026", "Oct 2026", "#260", "#261" }), "dated files attach to scheduled issues; numbered ones become issues");
        Assert.That(issues.Single(i => i.Number == "Sep 2026").IssueFileId, Is.Null);
        Assert.That(Directory.Exists(Path.Combine(_root, "Retro Gamer (2026)")), Is.False);
    }

    [Test]
    public void rescan_finds_new_files_and_forgets_deleted_ones()
    {
        Touch("Iron Tide (2022)/Iron Tide 001 (2022).cbz");
        var t = _titles.Add(new Title { Name = "Iron Tide", Kind = TitleKind.Comic, Year = 2022, Path = Path.Combine(_root, "Iron Tide (2022)") }, _root);

        var first = _scan.Scan(t.Id);
        Touch("Iron Tide (2022)/Iron Tide 002 (2022).cbz");
        File.Delete(Path.Combine(_root, "Iron Tide (2022)/Iron Tide 001 (2022).cbz"));
        var second = _scan.Scan(t.Id);

        Assert.That(first.Added, Is.EqualTo(1));
        Assert.That(second.Added, Is.EqualTo(1));
        Assert.That(second.Removed, Is.EqualTo(1));
        Assert.That(_issues.GetByTitle(t.Id).Where(i => i.IssueFileId != null).Select(i => i.Number), Is.EqualTo(new[] { "#2" }));
        Assert.That(_scan.Scan(t.Id).Summary, Is.EqualTo("No file changes"), "nothing new the third time");
    }

    [Test]
    public void better_format_replaces_worse_and_worse_is_skipped()
    {
        Touch("Neon Alley/Neon Alley 001.cbr");
        var t = _titles.Add(new Title { Name = "Neon Alley", Kind = TitleKind.Comic, Year = 2025, QualityProfile = "CBZ / CBR", Path = Path.Combine(_root, "Neon Alley") }, _root);
        _scan.Scan(t.Id);

        Touch("Neon Alley/Neon Alley 001 (digital).cbz");
        var r = _scan.Scan(t.Id);

        var file = _files.Get(_issues.GetByTitle(t.Id).Single().IssueFileId!.Value)!;
        Assert.That(file.Format, Is.EqualTo("CBZ"));
        Assert.That(r.Unmatched, Is.Empty);

        Touch("Neon Alley/Neon Alley 001 (scan).cbr");
        Assert.That(_scan.Scan(t.Id).Unmatched, Has.Count.EqualTo(2), "both CBRs are now duplicates of the CBZ");
    }

    [Test]
    public void missing_folder_is_reported()
    {
        var t = _titles.Add(new Title { Name = "Ghost", Path = Path.Combine(_root, "nope") }, _root);
        Assert.That(_scan.Scan(t.Id).Summary, Is.EqualTo("Folder not found on disk"));
    }

    [TestCase("Retro Gamer", true)]
    [TestCase("Retro Gamer (2004)", true)]
    [TestCase("retro_gamer", true)]
    [TestCase("Retro Gamer Collection", false)]
    public void existing_folder_detection(string folder, bool expected)
    {
        Directory.CreateDirectory(Path.Combine(_root, folder));
        Assert.That(TitleManagementService.ExistingFolder(_root, "Retro Gamer") != null, Is.EqualTo(expected));
    }
}
