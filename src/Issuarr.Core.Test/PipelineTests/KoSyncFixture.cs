using Issuarr.Core.Configuration;
using Issuarr.Core.Issues;
using Issuarr.Core.Reading;
using Issuarr.Core.Test.TitleTests;
using Issuarr.Core.Titles;
using Issuarr.Core.Users;
using Microsoft.Extensions.Logging.Abstractions;

namespace Issuarr.Core.Test.PipelineTests;

[TestFixture]
public class KoSyncFixture
{
    private TempDb _db = null!;
    private string _dir = "";
    private ConfigService _config = null!;
    private IssueRepository _issues = null!;
    private IssueFileRepository _files = null!;
    private ReadProgressRepository _progress = null!;
    private KoSyncService _sync = null!;
    private UserService _users = null!;
    private int _comicIssue;
    private int _bookIssue;

    [SetUp]
    public void SetUp()
    {
        _db = new TempDb();
        _dir = Directory.CreateTempSubdirectory("issuarr-ko-").FullName;
        var clock = new FixedClock(new DateOnly(2026, 11, 26));
        _config = new ConfigService(_db.Db);
        _issues = new IssueRepository(_db.Db);
        _files = new IssueFileRepository(_db.Db);
        var titles = new TitleService(new TitleRepository(_db.Db), _issues, clock);
        _progress = new ReadProgressRepository(_db.Db, clock);
        _users = new UserService(_db.Db, _config, new ConfigFile(new AppFolders(Path.Combine(_dir, "cfg"))), clock);
        _sync = new KoSyncService(_db.Db, _files, _issues, titles, _progress, clock, _users, NullLogger<KoSyncService>.Instance);

        var comic = titles.Add(new Title { Name = "Glass Cannon", Path = _dir }, _dir);
        File.WriteAllBytes(Path.Combine(_dir, "Glass Cannon 001 (2024).cbz"), Big());
        var f1 = _files.Insert(new IssueFile { TitleId = comic.Id, RelativePath = "Glass Cannon 001 (2024).cbz", Format = "CBZ", Size = 1, DateAdded = "x" });
        _issues.InsertMany([new Issue { TitleId = comic.Id, Number = "#1", SortKey = 1, ReleaseDate = "2024-01-01", IssueFileId = f1.Id }]);
        _comicIssue = _issues.GetByTitle(comic.Id).Single().Id;

        var author = titles.Add(new Title { Name = "Le Guin", Kind = TitleKind.Book, Path = _dir }, _dir);
        File.WriteAllBytes(Path.Combine(_dir, "lhod.epub"), Small());
        var f2 = _files.Insert(new IssueFile { TitleId = author.Id, RelativePath = "lhod.epub", Format = "EPUB", Size = 1, DateAdded = "x" });
        _issues.InsertMany([new Issue { TitleId = author.Id, Number = "The Left Hand of Darkness", SortKey = 1, ReleaseDate = "1969-01-01", IssueFileId = f2.Id }]);
        _bookIssue = _issues.GetByTitle(author.Id).Single().Id;

        _config.SaveMany(new Dictionary<string, string> { ["kosyncEnabled"] = "true", ["kosyncUsername"] = "reader", ["kosyncKey"] = KoSyncService.KeyFor("secret") });
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        Directory.Delete(_dir, true);
    }

    // Same bytes as the Python reference used to compute the expected hashes.
    private static byte[] Small() => Enumerable.Range(0, 3000).Select(i => (byte)((i * 7 + 3) % 256)).ToArray();
    private static byte[] Big() => Enumerable.Range(0, 300000).Select(i => (byte)((i * 31 + 11) % 251)).ToArray();

    [Test]
    public void partial_md5_matches_koreader()
    {
        Assert.That(KoSyncService.PartialMd5(Path.Combine(_dir, "lhod.epub")), Is.EqualTo("23398bb02c75378fb86bf1b62d860dee"));
        Assert.That(KoSyncService.PartialMd5(Path.Combine(_dir, "Glass Cannon 001 (2024).cbz")), Is.EqualTo("6bb97e2397876bf0bd42e3b404bae89a"));
        Assert.That(KoSyncService.FileNameMd5("Glass Cannon 001 (2024).cbz"), Is.EqualTo("af48ec627c7c2f95fb1dc97ef62518c5"));
    }

    [Test]
    public void auth_requires_enabled_matching_user_and_md5_key()
    {
        Assert.That(_sync.Authenticate("reader", KoSyncService.KeyFor("secret")), Is.EqualTo(User.OwnerId), "the original setup is the owner");
        Assert.That(_sync.Authenticate("reader", KoSyncService.KeyFor("secret").ToUpperInvariant()), Is.EqualTo(User.OwnerId));
        Assert.That(_sync.Authenticate("reader", KoSyncService.KeyFor("wrong")), Is.Null);
        Assert.That(_sync.Authenticate("someone", KoSyncService.KeyFor("secret")), Is.Null);
        _config.SaveMany(new Dictionary<string, string> { ["kosyncEnabled"] = "false" });
        Assert.That(_sync.Authenticate("reader", KoSyncService.KeyFor("secret")), Is.Null);
    }

    [Test]
    public void koreader_progress_updates_issuarr_progress()
    {
        _sync.Save(new KoSyncRecord { Document = "6bb97e2397876bf0bd42e3b404bae89a", Progress = "12", Percentage = 0.4, Device = "Kobo" });
        _sync.Save(new KoSyncRecord { Document = "23398bb02c75378fb86bf1b62d860dee", Progress = "/body/DocFragment[3]/body/p[4]/text().0", Percentage = 0.25, Device = "Kobo" });

        Assert.That(_progress.Get(User.OwnerId, _comicIssue)!.Position, Is.EqualTo("11"), "KOReader page 12 is Issuarr page index 11");
        Assert.That(_progress.Get(User.OwnerId, _comicIssue)!.Percent, Is.EqualTo(40).Within(0.01));
        Assert.That(_progress.Get(User.OwnerId, _bookIssue)!.Position, Is.EqualTo("pct:0.25"));
        Assert.That(_sync.Get(User.OwnerId, "23398bb02c75378fb86bf1b62d860dee")!.Progress, Does.StartWith("/body/"), "stored as sent, for other KOReader devices");
    }

    [Test]
    public void filename_method_also_matches()
    {
        _sync.Save(new KoSyncRecord { Document = KoSyncService.FileNameMd5("Glass Cannon 001 (2024).cbz"), Progress = "3", Percentage = 0.1 });
        Assert.That(_progress.Get(User.OwnerId, _comicIssue)!.Position, Is.EqualTo("2"));
    }

    [Test]
    public void web_reader_progress_is_offered_to_koreader_for_paged_formats()
    {
        const string doc = "6bb97e2397876bf0bd42e3b404bae89a";
        Assert.That(_sync.Get(User.OwnerId, doc), Is.Null, "nothing yet");

        _progress.Save(new ReadProgress { IssueId = _comicIssue, Position = "19", Percent = 80 });
        var offered = _sync.Get(User.OwnerId, doc)!;

        Assert.That(offered.Progress, Is.EqualTo("20"), "Issuarr page index 19 is KOReader page 20");
        Assert.That(offered.Percentage, Is.EqualTo(0.8));
        Assert.That(offered.Device, Is.EqualTo("Issuarr"));
    }

    [Test]
    public void unknown_documents_are_still_stored_for_device_to_device_sync()
    {
        _sync.Save(new KoSyncRecord { Document = "ffffffffffffffffffffffffffffffff", Progress = "/body/x", Percentage = 0.5, Device = "Phone" });
        Assert.That(_sync.Get(User.OwnerId, "ffffffffffffffffffffffffffffffff")!.Device, Is.EqualTo("Phone"));
    }

    [Test]
    public void each_user_syncs_their_own_positions()
    {
        var invite = _users.CreateInvite(Roles.Reader, "Sam", 7);
        var sam = _users.AcceptInvite(invite.Token, "sam", "password1");
        Assert.That(_sync.Authenticate("sam", KoSyncService.KeyFor("password1")), Is.Null, "the login password isn't the sync password");

        _users.SetKoSyncPassword(sam.Id, "kobo");
        Assert.That(_sync.Authenticate("sam", KoSyncService.KeyFor("kobo")), Is.EqualTo(sam.Id));
        Assert.That(_sync.Authenticate("SAM", KoSyncService.KeyFor("kobo")), Is.EqualTo(sam.Id), "usernames ignore case");

        const string doc = "6bb97e2397876bf0bd42e3b404bae89a";
        _sync.Save(new KoSyncRecord { Document = doc, Progress = "12", Percentage = 0.4, UserId = sam.Id });
        Assert.That(_progress.Get(sam.Id, _comicIssue)!.Percent, Is.EqualTo(40).Within(0.01));
        Assert.That(_progress.Get(User.OwnerId, _comicIssue), Is.Null, "the owner's place is untouched");
        Assert.That(_sync.Get(User.OwnerId, doc), Is.Null);
        Assert.That(_sync.Get(sam.Id, doc)!.Progress, Is.EqualTo("12"));

        _users.SetKoSyncPassword(sam.Id, null);
        Assert.That(_sync.Authenticate("sam", KoSyncService.KeyFor("kobo")), Is.Null, "turned off");
    }
}
