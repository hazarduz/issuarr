using Issuarr.Core.Blocklisting;
using Issuarr.Core.Configuration;
using Issuarr.Core.Decisions;
using Issuarr.Core.Download;
using Issuarr.Core.DownloadClients;
using Issuarr.Core.History;
using Issuarr.Core.Indexers;
using Issuarr.Core.Issues;
using Issuarr.Core.Metadata;
using Issuarr.Core.Notifications;
using Issuarr.Core.Organizer;
using Issuarr.Core.Providers;
using Issuarr.Core.Test.TitleTests;
using Issuarr.Core.Titles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Issuarr.Core.Test.PipelineTests;

/// <summary>A pretend download client that "downloads" instantly into a folder.</summary>
internal class FakeClient(string downloadDir) : IDownloadClient
{
    public List<DownloadItem> Items { get; } = [];
    public List<string> Removed { get; } = [];
    public string Implementation => "Fake";
    public string DisplayName => "Fake";
    public string Description => "";
    public DownloadProtocol Protocol => DownloadProtocol.Usenet;
    public IReadOnlyList<ProviderField> Fields => [];

    public Task<string> Add(ProviderDefinition def, ReleaseInfo release, CancellationToken ct)
    {
        var id = "nzo_" + Items.Count;
        var folder = Path.Combine(downloadDir, release.Title);
        Directory.CreateDirectory(folder);
        using (var zip = System.IO.Compression.ZipFile.Open(Path.Combine(folder, release.Title + ".cbz"), System.IO.Compression.ZipArchiveMode.Create))
        {
            using var page = zip.CreateEntry("001.jpg").Open();
            page.Write(new byte[256]);
        }

        Items.Add(new DownloadItem { DownloadId = id, Title = release.Title, TotalSize = 256, Status = DownloadItemStatus.Completed, OutputPath = folder });
        return Task.FromResult(id);
    }

    public Task<List<DownloadItem>> GetItems(ProviderDefinition def, CancellationToken ct) => Task.FromResult(Items.ToList());

    public Task Remove(ProviderDefinition def, string downloadId, bool deleteData, CancellationToken ct)
    {
        Removed.Add(downloadId);
        Items.RemoveAll(i => i.DownloadId == downloadId);
        return Task.CompletedTask;
    }

    public Task Test(ProviderDefinition def, CancellationToken ct) => Task.CompletedTask;
}

internal class NullNotifications : INotificationService
{
    public List<ImportMessage> Imports { get; } = [];
    public void Grabbed(GrabMessage message) { }
    public void Imported(ImportMessage message) => Imports.Add(message);
}

[TestFixture]
public class ScheduleAndImportFixture
{
    private TempDb _db = null!;
    private string _root = "";
    private FixedClock _clock = null!;
    private TitleRepository _titleRepo = null!;
    private TitleService _titles = null!;
    private IssueRepository _issues = null!;
    private IssueFileRepository _files = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new TempDb();
        _root = Directory.CreateTempSubdirectory("issuarr-pipe-").FullName;
        _clock = new FixedClock(new DateOnly(2026, 11, 26));
        _titleRepo = new TitleRepository(_db.Db);
        _issues = new IssueRepository(_db.Db);
        _files = new IssueFileRepository(_db.Db);
        _titles = new TitleService(_titleRepo, _issues, _clock);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        Directory.Delete(_root, true);
    }

    [Test]
    public async Task magazine_schedule_should_fill_issues_up_to_next_upcoming()
    {
        var refresh = new RefreshTitleService(_titleRepo, _issues, [], _clock, NullLogger<RefreshTitleService>.Instance);
        var t = _titles.Add(new Title { Name = "Trail Weekly", Kind = TitleKind.Magazine, Frequency = "Monthly", Year = 2026 }, _root);

        var added = await refresh.Refresh(t.Id, CancellationToken.None, new DateOnly(2026, 8, 1));
        var numbers = _issues.GetByTitle(t.Id).OrderBy(i => i.ReleaseDate).Select(i => i.Number).ToList();

        Assert.That(numbers, Is.EqualTo(new[] { "Aug 2026", "Sep 2026", "Oct 2026", "Nov 2026", "Dec 2026" }));
        Assert.That(added, Is.EqualTo(5));
        Assert.That(await refresh.Refresh(t.Id, CancellationToken.None), Is.EqualTo(0), "second refresh adds nothing new");
    }

    [Test]
    public async Task metadata_refresh_should_upsert_issues()
    {
        var provider = new FakeMetadata();
        var refresh = new RefreshTitleService(_titleRepo, _issues, [provider], _clock, NullLogger<RefreshTitleService>.Instance);
        var t = _titles.Add(new Title { Name = "Neon Alley", Kind = TitleKind.Comic, Year = 2025, MetadataSource = "fake", ExternalId = "77" }, _root);

        await refresh.Refresh(t.Id, CancellationToken.None);
        provider.Issues.Add(new IssueInfo { ExternalId = "3", Number = "3", StoreDate = "2026-12-10" });
        provider.Issues[0].Name = "Renamed";
        var added = await refresh.Refresh(t.Id, CancellationToken.None);

        var issues = _issues.GetByTitle(t.Id);
        Assert.That(added, Is.EqualTo(1));
        Assert.That(issues.Select(i => i.Number), Is.EqualTo(new[] { "#3", "#2", "#1" }));
        Assert.That(issues.Single(i => i.Number == "#1").IssueTitle, Is.EqualTo("Renamed"));
        Assert.That(_titles.Get(t.Id)!.Publisher, Is.EqualTo("Northlight"));
    }

    [Test]
    public async Task grab_then_process_should_import_rename_and_record_history()
    {
        var downloads = Path.Combine(_root, "downloads");
        var titlePath = Path.Combine(_root, "library", "Iron Tide (2022)");
        var t = _titles.Add(new Title { Name = "Iron Tide", Year = 2022, Kind = TitleKind.Comic, QualityProfile = "CBZ / CBR", Path = titlePath }, _root);
        _issues.InsertMany([new Issue { TitleId = t.Id, Number = "#46", SortKey = 46, ReleaseDate = "2026-11-20" }]);
        var issue = _issues.GetByTitle(t.Id).Single();

        var clientRepo = new DownloadClientRepository(_db.Db);
        clientRepo.Insert(new ProviderDefinition { Name = "SAB", Implementation = "Fake", Enable = true });
        var client = new FakeClient(downloads);
        var history = new HistoryService(_db.Db, _clock);
        var blocklist = new BlocklistService(_db.Db, _clock);
        var config = new ConfigService(_db.Db);
        var notifications = new NullNotifications();
        var tracked = new TrackedDownloadService(clientRepo, [client], history, NullLogger<TrackedDownloadService>.Instance);
        var grabber = new DownloadService(clientRepo, [client], history, notifications, NullLogger<DownloadService>.Instance);
        var completed = new CompletedDownloadService(tracked, _titles, _issues, _files, history, blocklist, config, new FileNameBuilder(config),
            notifications, [client], _clock, NullLogger<CompletedDownloadService>.Instance);

        var release = new ReleaseInfo { Guid = "1", Title = "Iron.Tide.046.2026.Digital", DownloadUrl = "http://x", Protocol = DownloadProtocol.Usenet, Indexer = "Geek" };
        await grabber.Grab(release, t, [issue], CancellationToken.None);
        Assert.That(tracked.PendingIssueIds(), Is.Empty, "not tracked until the queue is read");

        await tracked.Refresh(CancellationToken.None);
        await completed.Process(CancellationToken.None);

        var imported = Path.Combine(titlePath, "Iron Tide 046 (2026).cbz");
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(imported), Is.True, "file moved and renamed into the title folder");
            Assert.That(_issues.Get(issue.Id)!.IssueFileId, Is.Not.Null);
            Assert.That(history.ForDownload("nzo_0").Select(h => h.EventType), Is.EqualTo(new[] { HistoryEventType.Grabbed, HistoryEventType.Imported }));
            Assert.That(notifications.Imports, Has.Count.EqualTo(1));
            Assert.That(client.Removed, Is.EqualTo(new[] { "nzo_0" }), "usenet downloads are cleaned up after import");
            using var zip = System.IO.Compression.ZipFile.OpenRead(imported);
            Assert.That(zip.Entries.Any(e => e.Name == "ComicInfo.xml"), Is.True, "ComicInfo.xml written into the CBZ");
        });
    }

    [Test]
    public async Task failed_download_should_blocklist_and_ask_for_redownload()
    {
        var t = _titles.Add(new Title { Name = "Iron Tide", Year = 2022, Kind = TitleKind.Comic, Path = Path.Combine(_root, "lib") }, _root);
        _issues.InsertMany([new Issue { TitleId = t.Id, Number = "#46", SortKey = 46, ReleaseDate = "2026-11-20" }]);
        var issue = _issues.GetByTitle(t.Id).Single();
        var clientRepo = new DownloadClientRepository(_db.Db);
        clientRepo.Insert(new ProviderDefinition { Name = "SAB", Implementation = "Fake", Enable = true });
        var client = new FakeClient(Path.Combine(_root, "dl"));
        var history = new HistoryService(_db.Db, _clock);
        var blocklist = new BlocklistService(_db.Db, _clock);
        var config = new ConfigService(_db.Db);
        var tracked = new TrackedDownloadService(clientRepo, [client], history, NullLogger<TrackedDownloadService>.Instance);
        var grabber = new DownloadService(clientRepo, [client], history, new NullNotifications(), NullLogger<DownloadService>.Instance);
        var completed = new CompletedDownloadService(tracked, _titles, _issues, _files, history, blocklist, config, new FileNameBuilder(config),
            new NullNotifications(), [client], _clock, NullLogger<CompletedDownloadService>.Instance);

        await grabber.Grab(new ReleaseInfo { Guid = "1", Title = "Iron Tide 046 (2026) (bad)", DownloadUrl = "x", Protocol = DownloadProtocol.Usenet }, t, [issue], CancellationToken.None);
        client.Items[0].Status = DownloadItemStatus.Failed;
        client.Items[0].Message = "CRC errors";
        await tracked.Refresh(CancellationToken.None);
        var redownload = await completed.Process(CancellationToken.None);

        Assert.That(blocklist.IsBlocklisted(t.Id, "Iron Tide 046 (2026) (bad)"), Is.True);
        Assert.That(redownload.Single().IssueIds, Is.EqualTo(new[] { issue.Id }));
        Assert.That(history.ForDownload("nzo_0").Last().EventType, Is.EqualTo(HistoryEventType.DownloadFailed));
    }

    private class FakeMetadata : IMetadataProvider
    {
        public List<IssueInfo> Issues { get; } =
        [
            new() { ExternalId = "1", Number = "1", StoreDate = "2025-06-01" },
            new() { ExternalId = "2", Number = "2", CoverDate = "2025-07-01" },
        ];

        public string Source => "fake";
        public string DisplayName => "Fake";
        public IReadOnlyList<TitleKind> Kinds => [TitleKind.Comic];
        public bool IsConfigured => true;
        public Task<List<SeriesInfo>> Search(string query, CancellationToken ct) => Task.FromResult(new List<SeriesInfo>());
        public Task<SeriesInfo> GetSeries(string externalId, CancellationToken ct) => Task.FromResult(new SeriesInfo { Name = "Neon Alley", Publisher = "Northlight", Year = 2025 });
        public Task<List<IssueInfo>> GetIssues(string externalId, CancellationToken ct) => Task.FromResult(Issues.Select(i => new IssueInfo { ExternalId = i.ExternalId, Number = i.Number, Name = i.Name, StoreDate = i.StoreDate, CoverDate = i.CoverDate }).ToList());
    }
}
