using System.Net;
using Issuarr.Core.Blocklisting;
using Issuarr.Core.Configuration;
using Issuarr.Core.Decisions;
using Issuarr.Core.History;
using Issuarr.Core.Indexers;
using Issuarr.Core.Issues;
using Issuarr.Core.LibraryImport;
using Issuarr.Core.Metadata;
using Issuarr.Core.Organizer;
using Issuarr.Core.Parser;
using Issuarr.Core.Test.TitleTests;
using Issuarr.Core.Titles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Issuarr.Core.Test.PipelineTests;

[TestFixture]
public class BooksAndMagazinesFixture
{
    private static Issue Book(int id, string title) => new() { Id = id, Number = title };

    [TestCase("Brandon Sanderson - The Way of Kings (2010) [EPUB]", "The Way of Kings")]
    [TestCase("The.Way.of.Kings.by.Brandon.Sanderson.epub", "The Way of Kings")]
    [TestCase("Sanderson, Brandon - Words of Radiance (retail) mobi", "Words of Radiance")]
    [TestCase("Brandon Sanderson - Mistborn - The Final Empire.azw3", "Mistborn: The Final Empire")]
    [TestCase("Brandon Sanderson - Oathbringer (Stormlight 3) epub", "Oathbringer")]
    public void book_matcher_should_find_author_and_book(string release, string expected)
    {
        var author = new Title { Id = 1, Name = "Brandon Sanderson", Kind = TitleKind.Book };
        var books = new List<Issue> { Book(1, "The Way of Kings"), Book(2, "Words of Radiance"), Book(3, "Oathbringer"), Book(4, "Mistborn: The Final Empire"), Book(5, "Mistborn") };

        var m = BookMatcher.Match(release, [author], _ => books);

        Assert.That(m?.Book.Number, Is.EqualTo(expected));
    }

    [TestCase("Brandon Sanderson - Some Unknown Book (2030) epub")]
    [TestCase("Robert Jordan - The Way of Kings epub")]
    public void book_matcher_should_not_match(string release)
    {
        var author = new Title { Id = 1, Name = "Brandon Sanderson", Kind = TitleKind.Book };
        Assert.That(BookMatcher.Match(release, [author], _ => [Book(1, "The Way of Kings")]), Is.Null);
    }

    [Test]
    public async Task open_library_should_list_english_unique_works()
    {
        var http = new FakeHttp
        {
            Respond = (req, _) => req.RequestUri!.AbsolutePath switch
            {
                "/authors/OL1394244A.json" => FakeHttp.Text("""{"name":"Brandon Sanderson","bio":{"type":"/type/text","value":"American fantasy author."},"photos":[7891]}"""),
                "/search.json" => FakeHttp.Text("""
                    {"docs":[
                      {"key":"/works/OL1W","title":"Elantris","first_publish_year":2005,"cover_i":11,"language":["eng","ger"]},
                      {"key":"/works/OL2W","title":"Elantris","first_publish_year":2006,"language":["eng"]},
                      {"key":"/works/OL3W","title":"El camino de los reyes","first_publish_year":2011,"language":["spa"]},
                      {"key":"/works/OL4W","title":"The Way of Kings","first_publish_year":2010,"cover_i":22}
                    ]}
                    """),
                _ => FakeHttp.Text("", HttpStatusCode.NotFound),
            },
        };
        var ol = new OpenLibrary(new FakeHttpFactory(http));

        var author = await ol.GetSeries("OL1394244A", CancellationToken.None);
        var books = await ol.GetIssues("OL1394244A", CancellationToken.None);

        Assert.That(author.Name, Is.EqualTo("Brandon Sanderson"));
        Assert.That(author.Overview, Is.EqualTo("American fantasy author."));
        Assert.That(author.CoverUrl, Does.Contain("/a/id/7891"));
        Assert.That(books.Select(b => b.Name), Is.EqualTo(new[] { "Elantris", "The Way of Kings" }), "duplicate + Spanish edition removed");
        Assert.That(books[1].ExternalId, Is.EqualTo("OL4W"));
        Assert.That(books[1].CoverDate, Is.EqualTo("2010-01-01"));
    }

    [Test]
    public async Task wikidata_should_return_magazines_with_publisher_and_frequency()
    {
        var http = new FakeHttp
        {
            Respond = (req, _) =>
            {
                var q = req.RequestUri!.Query;
                if (q.Contains("wbsearchentities"))
                {
                    return FakeHttp.Text("""{"search":[{"id":"Q1"},{"id":"Q2"}]}""");
                }

                if (q.Contains("props=labels&"))
                {
                    return FakeHttp.Text("""{"entities":{"Q900":{"labels":{"en":{"value":"Future plc"}}}}}""");
                }

                return FakeHttp.Text("""
                    {"entities":{
                      "Q1":{"id":"Q1","labels":{"en":{"value":"Retro Gamer"}},"descriptions":{"en":{"value":"British video game magazine"}},
                        "claims":{
                          "P31":[{"mainsnak":{"datavalue":{"value":{"id":"Q41298"}}}}],
                          "P123":[{"mainsnak":{"datavalue":{"value":{"id":"Q900"}}}}],
                          "P571":[{"mainsnak":{"datavalue":{"value":{"time":"+2004-01-00T00:00:00Z"}}}}],
                          "P2896":[{"mainsnak":{"datavalue":{"value":{"amount":"+1","unit":"http://www.wikidata.org/entity/Q5151"}}}}],
                          "P154":[{"mainsnak":{"datavalue":{"value":"Retro Gamer logo.png"}}}]}},
                      "Q2":{"id":"Q2","labels":{"en":{"value":"Retro Gamer (song)"}},"descriptions":{"en":{"value":"song"}},
                        "claims":{"P31":[{"mainsnak":{"datavalue":{"value":{"id":"Q7366"}}}}]}}
                    }}
                    """);
            },
        };
        var wd = new Wikidata(new FakeHttpFactory(http));

        var results = await wd.Search("Retro Gamer", CancellationToken.None);

        var r = results.Single();
        Assert.Multiple(() =>
        {
            Assert.That(r.Name, Is.EqualTo("Retro Gamer"));
            Assert.That(r.Publisher, Is.EqualTo("Future plc"));
            Assert.That(r.Year, Is.EqualTo(2004));
            Assert.That(r.Frequency, Is.EqualTo("Monthly"));
            Assert.That(r.CoverUrl, Does.Contain("Special:FilePath/Retro%20Gamer%20logo.png"));
        });
    }

    private class FakeBooks : IMetadataProvider
    {
        public string Source => "openlibrary";
        public string DisplayName => "Open Library";
        public IReadOnlyList<TitleKind> Kinds => [TitleKind.Book];
        public bool IsConfigured => true;
        public Task<List<SeriesInfo>> Search(string q, CancellationToken ct) => Task.FromResult(new List<SeriesInfo>());
        public Task<SeriesInfo> GetSeries(string id, CancellationToken ct) => Task.FromResult(new SeriesInfo { Name = "Brandon Sanderson" });

        public Task<List<IssueInfo>> GetIssues(string id, CancellationToken ct) => Task.FromResult(new List<IssueInfo>
        {
            new() { ExternalId = "OL1W", Name = "Elantris", CoverDate = "2005-01-01" },
            new() { ExternalId = "OL4W", Name = "The Way of Kings", CoverDate = "2010-01-01" },
            new() { ExternalId = "OL9W", Name = "Wind and Truth", CoverDate = "2027-12-06" },
        });
    }

    private class WikidataMag : IMetadataProvider
    {
        public string Source => "wikidata";
        public string DisplayName => "Wikidata";
        public IReadOnlyList<TitleKind> Kinds => [TitleKind.Magazine];
        public bool IsConfigured => true;
        public Task<List<SeriesInfo>> Search(string q, CancellationToken ct) => Task.FromResult(new List<SeriesInfo>());
        public Task<SeriesInfo> GetSeries(string id, CancellationToken ct) => Task.FromResult(new SeriesInfo { Name = "Retro Gamer", Publisher = "Future plc", Frequency = "Monthly", Year = 2004 });
        public Task<List<IssueInfo>> GetIssues(string id, CancellationToken ct) => Task.FromResult(new List<IssueInfo>());
    }

    private TempDb _db = null!;
    private TitleService _titles = null!;
    private IssueRepository _issues = null!;
    private TitleManagementService _mgmt = null!;
    private RefreshTitleService _refresh = null!;
    private FixedClock _clock = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new TempDb();
        _clock = new FixedClock(new DateOnly(2026, 11, 26));
        var repo = new TitleRepository(_db.Db);
        _issues = new IssueRepository(_db.Db);
        _titles = new TitleService(repo, _issues, _clock);
        IMetadataProvider[] providers = [new FakeBooks(), new WikidataMag()];
        _refresh = new RefreshTitleService(repo, _issues, providers, _clock, NullLogger<RefreshTitleService>.Instance);
        _mgmt = new TitleManagementService(_titles, repo, providers, _refresh, _issues, new IssueFileRepository(_db.Db),
            new HistoryService(_db.Db, _clock), new FileNameBuilder(new ConfigService(_db.Db)), _clock);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    private static string Root => Path.GetTempPath();

    [Test]
    public async Task adding_one_book_monitors_only_that_book_then_another_book_adds_to_same_author()
    {
        var author = new Title { Name = "Brandon Sanderson", Kind = TitleKind.Book, MetadataSource = "openlibrary", ExternalId = "OL1A", Path = Path.Combine(Root, "issuarr-bs-" + Guid.NewGuid()) };
        var added = await _mgmt.Add(author, Root, new AddTitleOptions { BookExternalId = "OL4W" }, CancellationToken.None);

        var books = _issues.GetByTitle(added.Id);
        Assert.That(books.Select(b => b.Number), Is.EqualTo(new[] { "Wind and Truth", "The Way of Kings", "Elantris" }), "newest first");
        Assert.That(books.Where(b => b.Monitored).Select(b => b.Number), Is.EqualTo(new[] { "The Way of Kings" }));

        var again = await _mgmt.Add(new Title { Name = "Brandon Sanderson", Kind = TitleKind.Book, MetadataSource = "openlibrary", ExternalId = "OL1A" }, Root,
            new AddTitleOptions { BookTitle = "Elantris" }, CancellationToken.None);

        Assert.That(again.Id, Is.EqualTo(added.Id), "same author, not a duplicate title");
        Assert.That(_issues.GetByTitle(added.Id).Where(b => b.Monitored).Select(b => b.Number), Is.EquivalentTo(new[] { "The Way of Kings", "Elantris" }));
        Directory.Delete(added.Path, true);
    }

    [Test]
    public async Task wikidata_magazine_gets_details_and_scheduled_issues()
    {
        var t = _titles.Add(new Title { Name = "Retro Gamer", Kind = TitleKind.Magazine, MetadataSource = "wikidata", ExternalId = "Q1", Year = 2026 }, Root);
        _issues.InsertMany([new Issue { TitleId = t.Id, Number = "Sep 2026", SortKey = 20260901, ReleaseDate = "2026-09-01" }]);

        await _refresh.Refresh(t.Id, CancellationToken.None);

        var after = _titles.Get(t.Id)!;
        Assert.That(after.Publisher, Is.EqualTo("Future plc"));
        Assert.That(after.Frequency, Is.EqualTo("Monthly"));
        Assert.That(_issues.GetByTitle(t.Id).Select(i => i.Number), Is.EqualTo(new[] { "Dec 2026", "Nov 2026", "Oct 2026", "Sep 2026" }));
    }

    [Test]
    public async Task new_wikidata_magazine_starts_from_chosen_date()
    {
        var t = _titles.Add(new Title { Name = "Retro Gamer", Kind = TitleKind.Magazine, MetadataSource = "wikidata", ExternalId = "Q1", Year = 2026 }, Root);

        await _refresh.Refresh(t.Id, CancellationToken.None, new DateOnly(2026, 9, 1));

        Assert.That(_issues.GetByTitle(t.Id).Select(i => i.Number), Is.EqualTo(new[] { "Dec 2026", "Nov 2026", "Oct 2026", "Sep 2026" }));
    }

    [Test]
    public async Task book_releases_should_be_approved_with_ebook_profile()
    {
        var author = await _mgmt.Add(new Title { Name = "Brandon Sanderson", Kind = TitleKind.Book, QualityProfile = "eBook", MetadataSource = "openlibrary", ExternalId = "OL1A", Path = Path.Combine(Root, "issuarr-bs-" + Guid.NewGuid()) },
            Root, new AddTitleOptions { Monitor = MonitorOption.All }, CancellationToken.None);
        var decisions = new DecisionMaker(_titles, _issues, new IssueFileRepository(_db.Db), new BlocklistService(_db.Db, _clock), new ConfigService(_db.Db), new NoPending());

        var ds = decisions.Decide(
        [
            new ReleaseInfo { Guid = "1", Title = "Brandon Sanderson - The Way of Kings (2010) [EPUB]", Protocol = DownloadProtocol.Usenet },
            new ReleaseInfo { Guid = "2", Title = "Brandon Sanderson - Elantris (2005) CBR", Protocol = DownloadProtocol.Usenet },
            new ReleaseInfo { Guid = "3", Title = "Iron Tide 046 (2026)", Protocol = DownloadProtocol.Usenet },
        ], automatic: true, onlyTitle: author);

        Assert.That(ds[0].Approved, Is.True, string.Join("; ", ds[0].Rejections));
        Assert.That(ds[0].Format, Is.EqualTo("EPUB"));
        Assert.That(ds[1].Rejections, Has.Some.Contains("isn't allowed"));
        Assert.That(ds[2].Rejections, Has.Some.Contains("doesn't mention"));
        Directory.Delete(author.Path, true);
    }

    [TestCase("Brandon Sanderson - The Way of Kings (2010).epub", "The Way of Kings", "2010-01-01")]
    [TestCase("Elantris - Brandon Sanderson.mobi", "Elantris", null)]
    [TestCase("Mistborn_The_Final_Empire [retail].azw3", "Mistborn The Final Empire", null)]
    public void library_import_should_name_books_from_files(string file, string title, string? date)
    {
        var dir = Directory.CreateTempSubdirectory("issuarr-books-").FullName;
        var path = Path.Combine(dir, file);
        File.WriteAllText(path, "x");

        var issue = LibraryImportService.BookIssue(new FileInfo(path), "Brandon Sanderson", 0)!.Value.Issue;

        Assert.That(issue.Number, Is.EqualTo(title));
        if (date != null)
        {
            Assert.That(issue.ReleaseDate, Is.EqualTo(date));
        }

        Directory.Delete(dir, true);
    }

    [Test]
    public void book_file_names_use_book_format()
    {
        var names = new FileNameBuilder(new ConfigService(_db.Db));
        var author = new Title { Name = "Brandon Sanderson", Kind = TitleKind.Book };
        var book = new Issue { Number = "Mistborn: The Final Empire", ReleaseDate = "2006-07-17" };

        Assert.That(names.IssueFileName(author, book, ".epub"), Is.EqualTo("Brandon Sanderson - Mistborn - The Final Empire (2006).epub"));
        Assert.That(names.TitleFolder(author), Is.EqualTo("Brandon Sanderson"));
    }
}
