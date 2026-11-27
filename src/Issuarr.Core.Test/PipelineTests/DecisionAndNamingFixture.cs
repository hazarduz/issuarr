using Issuarr.Core.Blocklisting;
using Issuarr.Core.Configuration;
using Issuarr.Core.Decisions;
using Issuarr.Core.Indexers;
using Issuarr.Core.Issues;
using Issuarr.Core.Organizer;
using Issuarr.Core.Test.TitleTests;
using Issuarr.Core.Titles;

namespace Issuarr.Core.Test.PipelineTests;

internal class NoPending : IPendingDownloads
{
    public HashSet<int> Ids { get; } = [];
    public HashSet<int> PendingIssueIds() => Ids;
}

[TestFixture]
public class DecisionAndNamingFixture
{
    private TempDb _db = null!;
    private TitleService _titles = null!;
    private IssueRepository _issues = null!;
    private IssueFileRepository _files = null!;
    private BlocklistService _blocklist = null!;
    private ConfigService _config = null!;
    private NoPending _pending = null!;
    private DecisionMaker _decisions = null!;
    private Title _comic = null!;
    private Title _mag = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new TempDb();
        var clock = new FixedClock(new DateOnly(2026, 11, 26));
        _issues = new IssueRepository(_db.Db);
        _files = new IssueFileRepository(_db.Db);
        _titles = new TitleService(new TitleRepository(_db.Db), _issues, clock);
        _blocklist = new BlocklistService(_db.Db, clock);
        _config = new ConfigService(_db.Db);
        _pending = new NoPending();
        _decisions = new DecisionMaker(_titles, _issues, _files, _blocklist, _config, _pending);

        _comic = _titles.Add(new Title { Name = "Iron Tide", Year = 2022, Kind = TitleKind.Comic, QualityProfile = "CBZ / CBR" }, "/data");
        _mag = _titles.Add(new Title { Name = "Circuit & Solder", Year = 2015, Kind = TitleKind.Magazine, Frequency = "Monthly", QualityProfile = "PDF Preferred" }, "/data");
        _issues.InsertMany(
        [
            new Issue { TitleId = _comic.Id, Number = "#45", SortKey = 45, ReleaseDate = "2026-10-20" },
            new Issue { TitleId = _comic.Id, Number = "#46", SortKey = 46, ReleaseDate = "2026-11-20" },
            new Issue { TitleId = _mag.Id, Number = "Nov 2026", SortKey = 20261101, ReleaseDate = "2026-11-01" },
        ]);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    private static ReleaseInfo R(string title, DownloadProtocol p = DownloadProtocol.Torrent, int? seeders = 10) =>
        new() { Guid = title, Title = title, DownloadUrl = "http://x/" + title, Protocol = p, Seeders = seeders, Indexer = "T" };

    [Test]
    public void should_approve_matching_monitored_issue()
    {
        var d = _decisions.Decide([R("Iron Tide 046 (2026) (Digital) (Zone-Empire).cbz")], automatic: true).Single();

        Assert.That(d.Approved, Is.True, string.Join("; ", d.Rejections));
        Assert.That(d.Issue!.Number, Is.EqualTo("#46"));
    }

    [Test]
    public void should_match_dated_magazine_release()
    {
        var d = _decisions.Decide([R("Circuit.and.Solder.November.2026.PDF", DownloadProtocol.Usenet, null)], automatic: true).Single();

        Assert.That(d.Approved, Is.True, string.Join("; ", d.Rejections));
        Assert.That(d.Issue!.Number, Is.EqualTo("Nov 2026"));
    }

    [TestCase("Some Other Comic 046 (2026)", "isn't in your library")]
    [TestCase("Iron Tide 099 (2026)", "isn't known")]
    [TestCase("Iron Tide 046 (2026) PDF", "isn't allowed")]
    public void should_reject(string title, string reason)
    {
        var d = _decisions.Decide([R(title)], automatic: true).Single();
        Assert.That(d.Rejections, Has.Some.Contains(reason));
    }

    [Test]
    public void should_reject_low_seeders_blocklisted_and_pending()
    {
        _blocklist.Add(new BlocklistItem { TitleId = _comic.Id, SourceTitle = "Iron Tide 045 (2026) (bad)" });
        var issue46 = _issues.GetByTitle(_comic.Id).First(i => i.Number == "#46");
        _pending.Ids.Add(issue46.Id);

        var ds = _decisions.Decide([R("Iron Tide 045 (2026) (bad)"), R("Iron Tide 046 (2026)"), R("Iron Tide 045 (2026) (dead)", seeders: 0)], automatic: true);

        Assert.That(ds[0].Rejections, Has.Some.Contains("blocklisted"));
        Assert.That(ds[1].Rejections, Has.Some.Contains("Already downloading"));
        Assert.That(ds[2].Rejections, Has.Some.Contains("seeders"));
    }

    [Test]
    public void should_only_accept_upgrades()
    {
        var issue = _issues.GetByTitle(_comic.Id).First(i => i.Number == "#46");
        var file = _files.Insert(new IssueFile { TitleId = _comic.Id, RelativePath = "a.cbr", Format = "CBR", Size = 1, DateAdded = "x" });
        _issues.SetFile(issue.Id, file.Id);

        var ds = _decisions.Decide([R("Iron Tide 046 (2026).cbr"), R("Iron Tide 046 (2026).cbz")], automatic: true);

        Assert.That(ds[0].Rejections, Has.Some.Contains("Not an upgrade"));
        Assert.That(ds[1].Approved, Is.True, string.Join("; ", ds[1].Rejections));
    }

    [Test]
    public void prioritize_should_prefer_better_format_then_seeders()
    {
        var ds = _decisions.Decide([R("Iron Tide 046 (2026).cbr", seeders: 50), R("Iron Tide 046 (2026).cbz", seeders: 3), R("Iron Tide 046 (2026) (x).cbz", seeders: 30)], automatic: true);

        var best = _decisions.Prioritize(ds).First();
        Assert.That(best.Release.Title, Is.EqualTo("Iron Tide 046 (2026) (x).cbz"));
    }

    [Test]
    public void file_names_should_follow_formats()
    {
        var names = new FileNameBuilder(_config);
        var comicIssue = _issues.GetByTitle(_comic.Id).First(i => i.Number == "#46");
        var magIssue = _issues.GetByTitle(_mag.Id).Single();

        Assert.That(names.IssueFileName(_comic, comicIssue, ".CBZ"), Is.EqualTo("Iron Tide 046 (2026).cbz"));
        Assert.That(names.IssueFileName(_mag, magIssue, "pdf"), Is.EqualTo("Circuit & Solder - 2026-11.pdf"));
        Assert.That(names.TitleFolder(_comic), Is.EqualTo("Iron Tide (2022)"));

        _config.SaveMany(new Dictionary<string, string> { ["comicIssueFormat"] = "{Title}: #{Issue} {Issue Title} [{Format}]" });
        Assert.That(names.IssueFileName(_comic, comicIssue, "cbz"), Is.EqualTo("Iron Tide - #46 [CBZ].cbz"));
    }

    [TestCase("12.5", "#12.5")]
    [TestCase("001", "#1")]
    [TestCase("Annual 1", "#Annual 1")]
    public void should_normalize_metadata_issue_numbers(string raw, string expected) =>
        Assert.That(RefreshTitleService.NormalizeNumber(raw), Is.EqualTo(expected));
}
