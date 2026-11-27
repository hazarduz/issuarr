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
        Assert.That(h1.LatestComics.Select(i => i.Number), Is.EqualTo(new[] { "#4", "#3", "#2", "#1" }));
        Assert.That(h1.LatestMagazines, Is.Empty);
        Assert.That(h1.LatestBooks, Is.Empty);

        _progress.Save(new ReadProgress { IssueId = ids[1], Position = "5", Percent = 40 });
        var h2 = home.Get();
        Assert.That(h2.ContinueReading.Single().Number, Is.EqualTo("#2"));
        Assert.That(h2.ContinueReading.Single().Percent, Is.EqualTo(40));
        Assert.That(h2.UpNext, Is.Empty, "a title you're in the middle of isn't also up next");
    }

    /// <summary>A one-page PDF with a red rectangle, built with correct xref offsets.</summary>
    public static byte[] MinimalPdf()
    {
        var content = "1 0 0 rg 20 20 160 220 re f";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 260] /Contents 4 0 R >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
        };
        var sb = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var n = 0; n < objects.Length; n++)
        {
            offsets.Add(sb.Length);
            sb.Append($"{n + 1} 0 obj\n{objects[n]}\nendobj\n");
        }

        var xref = sb.Length;
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets)
        {
            sb.Append($"{o:D10} 00000 n \n");
        }

        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return System.Text.Encoding.ASCII.GetBytes(sb.ToString());
    }

    [Test]
    public void renders_and_caches_pdf_covers()
    {
        var t = _titles.Add(new Title { Name = "Retro Gamer", Kind = TitleKind.Magazine, Path = _dir }, _dir);
        var pdf = MinimalPdf();
        File.WriteAllBytes(Path.Combine(_dir, "0001 - Retro Gamer.pdf"), pdf);
        var f = _files.Insert(new IssueFile { TitleId = t.Id, RelativePath = "0001 - Retro Gamer.pdf", Format = "PDF", Size = pdf.Length, DateAdded = "x" });
        _issues.InsertMany([new Issue { TitleId = t.Id, Number = "#1", SortKey = 1, ReleaseDate = "2004-01-01", IssueFileId = f.Id }]);
        var issueId = _issues.GetByTitle(t.Id).Single().Id;

        var cover = _covers.IssueCover(issueId);
        Assert.That(cover, Is.Not.Null);
        Assert.That(cover!.Value.ContentType, Is.EqualTo("image/jpeg"));
        Assert.That(CoverService.ImageExtension(cover.Value.Data), Is.EqualTo(".jpg"));

        var cached = Path.Combine(_dir, "config", "MediaCover", "issues", $"{f.Id}-{pdf.Length}.jpg");
        Assert.That(File.Exists(cached), Is.True, "thumbnail cached");
        Assert.That(_covers.IssueCover(issueId)!.Value.Data, Is.EqualTo(cover.Value.Data));
        Assert.That(_covers.Candidates(t.Id).Select(c => c.Url), Does.Contain($"/api/v1/read/{issueId}/cover"));

        // A damaged PDF doesn't throw.
        Assert.That(CoverService.RenderPdfPage(Path.Combine(_dir, "nope.pdf")), Is.Null);
    }

    [Test]
    public void shrinks_big_comic_pages_to_cached_thumbnails()
    {
        var t = _titles.Add(new Title { Name = "Iron Tide", Kind = TitleKind.Comic, Path = _dir }, _dir);
        byte[] page;
        using (var bmp = new SkiaSharp.SKBitmap(2000, 3000))
        {
            bmp.Erase(SkiaSharp.SKColors.SteelBlue);
            using var img = SkiaSharp.SKImage.FromBitmap(bmp);
            page = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100).ToArray();
        }

        var cbz = Path.Combine(_dir, "1.cbz");
        using (var z = ZipFile.Open(cbz, ZipArchiveMode.Create))
        using (var e = z.CreateEntry("001.png").Open())
        {
            e.Write(page);
        }

        var f = _files.Insert(new IssueFile { TitleId = t.Id, RelativePath = "1.cbz", Format = "CBZ", Size = new FileInfo(cbz).Length, DateAdded = "x" });
        _issues.InsertMany([new Issue { TitleId = t.Id, Number = "#1", SortKey = 1, ReleaseDate = "2026-01-01", IssueFileId = f.Id }]);

        var cover = _covers.IssueCover(_issues.GetByTitle(t.Id).Single().Id)!.Value;
        using var thumb = SkiaSharp.SKBitmap.Decode(cover.Data);
        Assert.That(cover.ContentType, Is.EqualTo("image/jpeg"));
        Assert.That((thumb.Width, thumb.Height), Is.EqualTo((600, 900)));
    }
}
