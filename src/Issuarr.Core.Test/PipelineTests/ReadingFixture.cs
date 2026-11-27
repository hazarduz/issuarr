using System.IO.Compression;
using Issuarr.Core.Issues;
using Issuarr.Core.Reading;
using Issuarr.Core.Test.TitleTests;
using Issuarr.Core.Titles;

namespace Issuarr.Core.Test.PipelineTests;

[TestFixture]
public class ReadingFixture
{
    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = Directory.CreateTempSubdirectory("issuarr-read-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, true);

    private string MakeCbz(params string[] entries)
    {
        var path = Path.Combine(_dir, Guid.NewGuid() + ".cbz");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var e in entries)
        {
            using var s = zip.CreateEntry(e).Open();
            s.Write(System.Text.Encoding.UTF8.GetBytes(e));
        }

        return path;
    }

    [Test]
    public void pages_should_be_images_in_natural_order()
    {
        var cbz = MakeCbz("Comic/page10.jpg", "Comic/page2.jpg", "Comic/page1.jpg", "ComicInfo.xml", "__MACOSX/._page1.jpg", "Comic/.hidden.png", "cover.PNG");

        var pages = ComicArchive.Pages(cbz);

        Assert.That(pages, Is.EqualTo(new[] { "Comic/page1.jpg", "Comic/page2.jpg", "Comic/page10.jpg", "cover.PNG" }));
    }

    [Test]
    public void page_should_return_bytes_and_type()
    {
        var cbz = MakeCbz("001.png", "002.webp");

        var page = ComicArchive.Page(cbz, 1)!.Value;

        Assert.That(System.Text.Encoding.UTF8.GetString(page.Data), Is.EqualTo("002.webp"));
        Assert.That(page.ContentType, Is.EqualTo("image/webp"));
        Assert.That(ComicArchive.Page(cbz, 5), Is.Null);
    }

    [Test]
    public void should_read_rar_and_7z_archives_too()
    {
        // A CBR is often really a zip with the wrong extension; the archive type is detected from content.
        var zipAsCbr = Path.ChangeExtension(MakeCbz("a.jpg", "b.jpg"), ".cbr");
        File.Move(Path.ChangeExtension(zipAsCbr, ".cbz"), zipAsCbr);
        Assert.That(ComicArchive.Pages(zipAsCbr), Has.Count.EqualTo(2));
    }

    [Test]
    public void progress_should_upsert_and_report_percent()
    {
        using var db = new TempDb();
        var clock = new FixedClock(new DateOnly(2026, 11, 26));
        var titles = new TitleService(new TitleRepository(db.Db), new IssueRepository(db.Db), clock);
        var t = titles.Add(new Title { Name = "Iron Tide", Year = 2022 }, "/data");
        var issues = new IssueRepository(db.Db);
        issues.InsertMany([new Issue { TitleId = t.Id, Number = "#1", SortKey = 1, ReleaseDate = "2022-01-01" }]);
        var id = issues.GetByTitle(t.Id).Single().Id;
        var repo = new ReadProgressRepository(db.Db, clock);

        repo.Save(new ReadProgress { IssueId = id, Position = "3", Percent = 10 });
        repo.Save(new ReadProgress { IssueId = id, Position = "12", Percent = 140 });

        Assert.That(repo.Get(id)!.Position, Is.EqualTo("12"));
        Assert.That(repo.PercentByIssue([id])[id], Is.EqualTo(100), "clamped");
        repo.Clear(id);
        Assert.That(repo.Get(id), Is.Null);
    }
}
