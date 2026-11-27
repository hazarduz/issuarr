using Issuarr.Core.Configuration;
using Issuarr.Core.Issues;
using Issuarr.Core.Reading;
using Issuarr.Core.Test.TitleTests;
using Issuarr.Core.Titles;
using Issuarr.Core.Users;

namespace Issuarr.Core.Test.PipelineTests;

[TestFixture]
public class UsersFixture
{
    private TempDb _db = null!;
    private string _dir = "";
    private ConfigFile _file = null!;
    private UserService _users = null!;
    private FixedClock _clock = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new TempDb();
        _dir = Directory.CreateTempSubdirectory("issuarr-users-").FullName;
        _clock = new FixedClock(new DateOnly(2026, 11, 27));
        _file = new ConfigFile(new AppFolders(_dir));
        _file.SetMany(new Dictionary<string, string> { ["Username"] = "martin" });
        _file.SetPassword("ownerpass");
        _users = new UserService(_db.Db, new ConfigService(_db.Db), _file, _clock);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        Directory.Delete(_dir, true);
    }

    [Test]
    public void owner_is_the_config_login()
    {
        var owner = _users.Verify("Martin", "ownerpass")!;
        Assert.That(owner.Id, Is.EqualTo(User.OwnerId));
        Assert.That(owner.Username, Is.EqualTo("martin"));
        Assert.That(owner.IsAdmin, Is.True);
        Assert.That(_users.Verify("martin", "nope"), Is.Null);
    }

    [Test]
    public void invite_creates_a_reader_once()
    {
        var invite = _users.CreateInvite(Roles.Reader, "Sam", 7);
        Assert.That(_users.PendingInvites().Single().Note, Is.EqualTo("Sam"));

        var sam = _users.AcceptInvite(invite.Token, " sam ", "hunter22");
        Assert.That(sam.Username, Is.EqualTo("sam"));
        Assert.That(sam.Role, Is.EqualTo(Roles.Reader));
        Assert.That(_users.Verify("SAM", "hunter22")?.Id, Is.EqualTo(sam.Id));
        Assert.That(_users.PendingInvites(), Is.Empty, "used up");
        Assert.Throws<ArgumentException>(() => _users.AcceptInvite(invite.Token, "sam2", "hunter22"));
    }

    [Test]
    public void invites_validate_usernames_passwords_and_expiry()
    {
        var a = _users.CreateInvite(Roles.Reader, null, 1);
        Assert.Throws<ArgumentException>(() => _users.AcceptInvite(a.Token, "martin", "longenough"), "owner's name is taken");
        Assert.Throws<ArgumentException>(() => _users.AcceptInvite(a.Token, "has space", "longenough"));
        Assert.Throws<ArgumentException>(() => _users.AcceptInvite(a.Token, "ok", "short"));
        Assert.That(_users.ValidInvite(a.Token), Is.Not.Null, "failed attempts don't use the invite");

        _clock.UtcNow = _clock.UtcNow.AddDays(2);
        Assert.That(_users.ValidInvite(a.Token), Is.Null, "expired");
        Assert.Throws<ArgumentException>(() => _users.AcceptInvite(a.Token, "late", "longenough"));
    }

    [Test]
    public void roles_passwords_and_deleting()
    {
        var sam = _users.AcceptInvite(_users.CreateInvite(Roles.Reader, null, 7).Token, "sam", "hunter22");
        _users.SetRole(sam.Id, Roles.Admin);
        Assert.That(_users.Get(sam.Id)!.IsAdmin, Is.True);
        Assert.Throws<ArgumentException>(() => _users.SetRole(User.OwnerId, Roles.Reader));
        Assert.Throws<ArgumentException>(() => _users.Delete(User.OwnerId));

        Assert.Throws<ArgumentException>(() => _users.ChangeOwnPassword(sam.Id, "wrong", "newpass1"));
        _users.ChangeOwnPassword(sam.Id, "hunter22", "newpass1");
        Assert.That(_users.Verify("sam", "newpass1"), Is.Not.Null);

        _users.ChangeOwnPassword(User.OwnerId, "ownerpass", "ownerpass2");
        Assert.That(_file.VerifyPassword("martin", "ownerpass2"), Is.True, "the owner's password lives in config.xml");

        _users.Delete(sam.Id);
        Assert.That(_users.Get(sam.Id), Is.Null);
        Assert.That(_users.Verify("sam", "newpass1"), Is.Null);
    }

    [Test]
    public void reading_progress_and_home_are_per_user()
    {
        var titles = new TitleService(new TitleRepository(_db.Db), new IssueRepository(_db.Db), _clock);
        var issues = new IssueRepository(_db.Db);
        var files = new IssueFileRepository(_db.Db);
        var progress = new ReadProgressRepository(_db.Db, _clock);
        var t = titles.Add(new Title { Name = "Iron Tide", Kind = TitleKind.Comic, Path = _dir }, _dir);
        for (var n = 1; n <= 2; n++)
        {
            var f = files.Insert(new IssueFile { TitleId = t.Id, RelativePath = $"{n}.cbz", Format = "CBZ", Size = 1, DateAdded = "2026-11-01T00:00:00Z" });
            issues.InsertMany([new Issue { TitleId = t.Id, Number = $"#{n}", SortKey = n, ReleaseDate = "2026-01-01", IssueFileId = f.Id }]);
        }

        var ids = issues.GetByTitle(t.Id).OrderBy(i => i.SortKey).Select(i => i.Id).ToList();
        var sam = _users.AcceptInvite(_users.CreateInvite(Roles.Reader, null, 7).Token, "sam", "hunter22");
        progress.Save(new ReadProgress { UserId = User.OwnerId, IssueId = ids[0], Position = "3", Percent = 30 });
        progress.Save(new ReadProgress { UserId = sam.Id, IssueId = ids[0], Position = "end", Percent = 100 });

        var home = new ReadingHomeService(progress, issues, files, titles);
        Assert.That(home.Get(User.OwnerId).ContinueReading.Single().Percent, Is.EqualTo(30));
        Assert.That(home.Get(User.OwnerId).UpNext, Is.Empty);
        Assert.That(home.Get(sam.Id).ContinueReading, Is.Empty);
        Assert.That(home.Get(sam.Id).UpNext.Single().Number, Is.EqualTo("#2"), "Sam finished #1");

        _users.Delete(sam.Id);
        Assert.That(progress.All(sam.Id), Is.Empty, "deleting a user removes their progress");
        Assert.That(progress.All(User.OwnerId), Has.Count.EqualTo(1));
    }
}
