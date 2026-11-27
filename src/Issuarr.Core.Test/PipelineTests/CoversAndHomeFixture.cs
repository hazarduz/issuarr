using System.IO.Compression;
using Issuarr.Core.Configuration;
using Issuarr.Core.Issues;
using Issuarr.Core.MediaFiles;
using Issuarr.Core.Reading;
using Issuarr.Core.Test.TitleTests;
using Issuarr.Core.Titles;

namespace Issuarr.Core.Test.PipelineTests;

[TestFixture]
public class CoversAndHomeFixture
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 1, 2, 3];
    private TempDb _db = null!;
    private string _dir = "";
    private TitleRepository _repo = null!;
    private TitleService _titles = null!;
    private IssueRepository _issues = null!;
    private IssueFileRepository _files = null!;
    private CoverService _covers = null!;
    private ReadProgressRepository _progress = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new TempDb();
        _dir = Directory.CreateTempSubdirectory("issuarr-cov-").FullName;
        var clock = new FixedClock(new DateOnly(2026, 11, 26));
        _repo = new TitleRepository(_db.Db);
        _issues = new IssueRepository(_db.Db);
        _files = new IssueFileRepository(_db.Db);
        _titles = new TitleService(_repo, _issues, clock);
        _covers = new CoverService(new AppFolders(Path.Combine(_dir, "config")), _repo, _issues, _files, new FakeHttpFactory(new FakeHttp()));
        _progress = new ReadProgressRepository(_db.Db, clock);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        Directory.Delete(_dir, true);
    }

    private string Epub(string name, bool epub3)
    {
        var path = Path.Combine(_dir, name);
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(string n, byte[] d)
        {
            using var s = z.CreateEntry(n).Open();
            s.Write(d);
        }

        Put("META-INF/container.xml", "<?xml version=\"1.0\"?><container xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\"/></rootfiles></container>"u8.ToArray());
        var manifest = epub3
            ? "<item id=\"img\" href=\"images/front.png\" media-type=\"image/png\" properties=\"cover-image\"/>"
            : "<item id=\"cov\" href=\"images/front.png\" media-type=\"image/png\"/>";
        var meta = epub3 ? "" : "<meta name=\"cover\" content=\"cov\"/>";
        Put("OEBPS/content.opf", System.Text.Encoding.UTF8.GetBytes($"<package xmlns=\"http://www.idpf.org/2007/opf\"><metadata>{meta}</metadata><manifest>{manifest}</manifest></package>"));
        Put("OEBPS/images/front.png", Png);
        return path;
    }

    [TestCase(true)]
    [TestCase(false)]
    public void should_find_epub_cover(bool epub3)
    {
        var cover = CoverService.EpubCover(Epub($"book{epub3}.epub", epub3));
        Assert.That(cover?.Data, Is.EqualTo(Png));
        Assert.That(cover?.ContentType, Is.EqualTo("image/png"));
    }

    [Test]
    public async Task custom_cover_upload_replace_and_reset()
    {
        var t = _titles.Add(new Title { Name = "Retro Gamer", Path = _dir }, _dir);

        Assert.ThrowsAsync<ArgumentException>(() => _covers.SetFromBytes(t.Id, "not an image at all"u8.ToArray(), CancellationToken.None));
        await _covers.SetFromBytes(t.Id, Png, CancellationToken.None);
        var first = _covers.CustomCoverPath(_titles.Get(t.Id)!)!;
        await _covers.SetFromBytes(t.Id, Png, CancellationToken.None);
        var second = _covers.CustomCoverPath(_titles.Get(t.Id)!)!;

        Assert.That(File.Exists(first), Is.False, "old cover removed");
        Assert.That(File.Exists(second), Is.True);
        Assert.That(second, Does.EndWith(".png"));

        _covers.Reset(t.Id);
        Assert.That(_titles.Get(t.Id)!.CustomCover, Is.Null);
        Assert.That(File.Exists(second), Is.False);
    }

    [Test]
    public async Task can_use_a_files_cover_as_the_title_cover()
    {
        var t = _titles.Add(new Title { Name = "Le Guin", Kind = TitleKind.Book, Path = _dir }, _dir);
        Epub("lhod.epub", true);
        var f = _files.Insert(new IssueFile { TitleId = t.Id, RelativePath = "lhod.epub", Format = "EPUB", Size = 1, DateAdded = "x" });
        _issues.InsertMany([new Issue { TitleId = t.Id, Number = "The Left Hand of Darkness", SortKey = 1, ReleaseDate = "1969-01-01", IssueFileId = f.Id }]);
        var issueId = _issues.GetByTitle(t.Id).Single().Id;

        var candidates = _covers.Candidates(t.Id);
        Assert.That(candidates.Select(c => c.Url), Does.Contain($"/api/v1/read/{issueId}/cover"));

        await _covers.SetFromUrl(t.Id, $"/api/v1/read/{issueId}/cover", CancellationToken.None);
        Assert.That(File.ReadAllBytes(_covers.CustomCoverPath(_titles.Get(t.Id)!)!), Is.EqualTo(Png));
    }

    [Test]
    public void home_shelves()
    {
        var t = _titles.Add(new Title { Name = "Iron Tide", Kind = TitleKind.Comic, Path = _dir }, _dir);
        var ids = new List<int>();
        for (var n = 1; n <= 4; n++)
        {
            var f = _files.Insert(new IssueFile { TitleId = t.Id, RelativePath = $"{n}.cbz", Format = "CBZ", Size = 1, DateAdded = $"2026-11-0{n}T00:00:00Z" });
            _issues.InsertMany([new Issue { TitleId = t.Id, Number = $"#{n}", SortKey = n, ReleaseDate = "2026-01-01", IssueFileId = f.Id }]);
        }

        ids.AddRange(_issues.GetByTitle(t.Id).OrderBy(i => i.SortKey).Select(i => i.Id));
        var home = new ReadingHomeService(_progress, _issues, _files, _titles);

        _progress.Save(new ReadProgress { IssueId = ids[0], Position = "end", Percent = 100 });
        var h1 = home.Get();
        Assert.That(h1.ContinueReading, Is.Empty);
        Assert.That(h1.UpNext.Single().Number, Is.EqualTo("#2"), "after finishing #1, #2 is up next");
        Assert.That(h1.RecentlyAdded.First().Number, Is.EqualTo("#4"));

        _progress.Save(new ReadProgress { IssueId = ids[1], Position = "5", Percent = 40 });
        var h2 = home.Get();
        Assert.That(h2.ContinueReading.Single().Number, Is.EqualTo("#2"));
        Assert.That(h2.ContinueReading.Single().Percent, Is.EqualTo(40));
        Assert.That(h2.UpNext, Is.Empty, "a title you're in the middle of isn't also up next");
    }
}
