using Issuarr.Core.Blocklisting;
using Issuarr.Core.Configuration;
using Issuarr.Core.Decisions;
using Issuarr.Core.DownloadClients;
using Issuarr.Core.History;
using Issuarr.Core.Indexers;
using Issuarr.Core.Issues;
using Issuarr.Core.LibraryImport;
using Issuarr.Core.MediaFiles;
using Issuarr.Core.Notifications;
using Issuarr.Core.Organizer;
using Issuarr.Core.Parser;
using Issuarr.Core.Providers;
using Issuarr.Core.Titles;
using Microsoft.Extensions.Logging;

namespace Issuarr.Core.Download;

public interface ICompletedDownloadService
{
    /// <summary>Imports finished downloads and handles failed ones. Returns titles/issues that need a new search.</summary>
    Task<List<(int TitleId, List<int> IssueIds)>> Process(CancellationToken ct);

    Task RemoveFromQueue(string key, bool removeFromClient, bool blocklist, CancellationToken ct);
}

public class CompletedDownloadService(
    ITrackedDownloadService tracked,
    ITitleService titles,
    IIssueRepository issues,
    IIssueFileRepository files,
    IHistoryService history,
    IBlocklistService blocklist,
    IConfigService config,
    FileNameBuilder names,
    INotificationService notifications,
    IEnumerable<IDownloadClient> clientImpls,
    IClock clock,
    ILogger<CompletedDownloadService> log) : ICompletedDownloadService
{
    public async Task<List<(int TitleId, List<int> IssueIds)>> Process(CancellationToken ct)
    {
        var redownload = new List<(int, List<int>)>();
        foreach (var t in tracked.All())
        {
            if (t.Item.Status == DownloadItemStatus.Failed && t.State != TrackedState.Failed && t.TitleId != null)
            {
                MarkFailed(t, t.Item.Message ?? "Download client reported a failure");
                if (config.AutoRedownloadFailed && t.IssueIds.Count > 0)
                {
                    redownload.Add((t.TitleId.Value, t.IssueIds));
                }

                continue;
            }

            if (t.State == TrackedState.ImportPending)
            {
                await Import(t, ct);
            }
        }

        return redownload;
    }

    private async Task Import(TrackedDownload t, CancellationToken ct)
    {
        tracked.SetState(t, TrackedState.Importing);
        var output = t.Item.OutputPath;
        if (string.IsNullOrEmpty(output) || (!File.Exists(output) && !Directory.Exists(output)))
        {
            tracked.SetState(t, TrackedState.ImportFailed,
                $"Can't find the downloaded files at \"{output}\". If the download client runs in another container, set a remote path mapping on the download client.");
            return;
        }

        var candidates = File.Exists(output)
            ? [new FileInfo(output)]
            : new DirectoryInfo(output).EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(f => LibraryImportService.Extensions.Contains(f.Extension) && !f.Name.Contains("sample", StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (candidates.Count == 0)
        {
            tracked.SetState(t, TrackedState.ImportFailed, "No CBZ, CBR, CB7, PDF or EPUB files in the download");
            return;
        }

        var title = t.TitleId is { } id ? titles.Get(id) : FindTitle(t.Item.Title);
        if (title?.Kind == TitleKind.Book && candidates.Count > 1)
        {
            // Releases often bundle EPUB + MOBI + PDF; keep the best format for the profile.
            var profileForBook = QualityProfiles.Get(title.QualityProfile);
            candidates = candidates.OrderBy(f => profileForBook.Rank(f.Extension.TrimStart('.'))).ThenByDescending(f => f.Length).Take(1).ToList();
        }
        if (title == null)
        {
            tracked.SetState(t, TrackedState.ImportFailed, $"Couldn't match \"{t.Item.Title}\" to a title in your library");
            return;
        }

        var titleIssues = issues.GetByTitle(title.Id);
        var titleFiles = files.GetByTitle(title.Id).ToDictionary(f => f.Id);
        var profile = QualityProfiles.Get(title.QualityProfile);
        var imported = 0;
        var messages = new List<string>();

        foreach (var file in candidates)
        {
            var issue = MatchIssue(file, t, candidates.Count, titleIssues, title);
            if (issue == null)
            {
                messages.Add($"{file.Name}: couldn't tell which issue this is");
                continue;
            }

            var format = file.Extension.TrimStart('.').ToUpperInvariant();
            IssueFile? existing = issue.IssueFileId is { } fid ? titleFiles.GetValueOrDefault(fid) : null;
            if (existing != null && profile.Rank(existing.Format) <= profile.Rank(format))
            {
                messages.Add($"{file.Name}: not an upgrade over the existing {existing.Format}");
                continue;
            }

            var destination = Path.Combine(title.Path, names.IssueFileName(title, issue, file.Extension));
            var mode = t.Protocol == DownloadProtocol.Torrent
                ? (config.UseHardlinks ? TransferMode.HardLinkOrCopy : TransferMode.Copy)
                : TransferMode.Move;

            try
            {
                if (existing != null)
                {
                    DeleteFile(title, existing);
                }

                var done = DiskTransfer.Transfer(file.FullName, destination, mode, overwrite: true);

                // Never modify a hardlinked file: it's the same file the torrent is seeding.
                if (config.WriteComicInfo && format == "CBZ" && done != TransferMode.HardLinkOrCopy)
                {
                    ComicInfoWriter.TryWrite(destination, title, issue);
                }

                var stored = files.Insert(new IssueFile
                {
                    TitleId = title.Id,
                    RelativePath = Path.GetRelativePath(title.Path, destination),
                    Size = new FileInfo(destination).Length,
                    Format = format,
                    DateAdded = clock.UtcNow.ToString("O"),
                });
                issues.SetFile(issue.Id, stored.Id);
                issue.IssueFileId = stored.Id;
                titleFiles[stored.Id] = stored;

                history.Add(new HistoryItem
                {
                    TitleId = title.Id,
                    IssueId = issue.Id,
                    EventType = HistoryEventType.Imported,
                    SourceTitle = t.SourceTitle,
                    DownloadId = t.Item.DownloadId,
                    Data = new() { ["path"] = destination, ["downloadClient"] = t.Client.Name, ["upgrade"] = (existing != null).ToString() },
                });
                notifications.Imported(new ImportMessage(title, issue, destination, existing != null));
                imported++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                messages.Add($"{file.Name}: {e.Message}");
                log.LogWarning("Import of {File} failed: {Message}", file.FullName, e.Message);
            }
        }

        if (imported == 0)
        {
            tracked.SetState(t, TrackedState.ImportFailed, messages.ToArray());
            history.Add(new HistoryItem
            {
                TitleId = title.Id,
                IssueId = t.IssueIds.FirstOrDefault() is var first and > 0 ? first : null,
                EventType = HistoryEventType.ImportFailed,
                SourceTitle = t.SourceTitle,
                DownloadId = t.Item.DownloadId,
                Data = new() { ["message"] = string.Join("; ", messages) },
            });
            return;
        }

        tracked.SetState(t, TrackedState.Imported, messages.ToArray());
        var removeFromClient = t.Protocol == DownloadProtocol.Usenet || config.RemoveCompletedDownloads;
        if (removeFromClient && Client(t) is { } client)
        {
            try
            {
                await client.Remove(t.Client, t.Item.DownloadId, deleteData: t.Protocol == DownloadProtocol.Usenet, ct);
            }
            catch (Exception e)
            {
                log.LogWarning("Couldn't remove {Download} from {Client}: {Message}", t.Item.Title, t.Client.Name, e.Message);
            }
        }
    }

    private Issue? MatchIssue(FileInfo file, TrackedDownload t, int fileCount, List<Issue> titleIssues, Title title)
    {
        if (title.Kind == TitleKind.Book)
        {
            var n = BookMatcher.Normalize(Path.GetFileNameWithoutExtension(file.Name));
            var byBookName = BookMatcher.MatchBook(n, titleIssues) ?? BookMatcher.MatchBook(BookMatcher.Normalize(t.SourceTitle), titleIssues);
            if (byBookName != null)
            {
                return byBookName;
            }

            // An ebook download often has several formats of the same book; trust the grab.
            return t.IssueIds.Count == 1 ? titleIssues.FirstOrDefault(i => i.Id == t.IssueIds[0]) : null;
        }

        var parsed = ReleaseParser.Parse(file.Name);
        var byName = parsed == null ? null : DecisionMaker.MatchIssue(parsed, titleIssues);
        if (byName != null)
        {
            return byName;
        }

        // A single-file download for a single grabbed issue: trust the grab.
        if (fileCount == 1 && t.IssueIds.Count == 1)
        {
            return titleIssues.FirstOrDefault(i => i.Id == t.IssueIds[0]);
        }

        var fromRelease = ReleaseParser.Parse(t.SourceTitle);
        return fileCount == 1 && fromRelease != null ? DecisionMaker.MatchIssue(fromRelease, titleIssues) : null;
    }

    private Title? FindTitle(string downloadTitle)
    {
        var all = titles.All();
        if (BookMatcher.Match(downloadTitle, all, t => issues.GetByTitle(t.Id)) is { } book)
        {
            return book.Title;
        }

        var parsed = ReleaseParser.Parse(downloadTitle);
        return parsed == null ? null : titles.FindByReleaseTitle(parsed.TitleName);
    }

    private void DeleteFile(Title title, IssueFile file)
    {
        var path = Path.Combine(title.Path, file.RelativePath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        issues.ClearFile(file.Id);
        files.Delete(file.Id);
    }

    private void MarkFailed(TrackedDownload t, string message)
    {
        tracked.SetState(t, TrackedState.Failed, message);
        history.Add(new HistoryItem
        {
            TitleId = t.TitleId!.Value,
            IssueId = t.IssueIds.FirstOrDefault() is var first and > 0 ? first : null,
            EventType = HistoryEventType.DownloadFailed,
            SourceTitle = t.SourceTitle,
            DownloadId = t.Item.DownloadId,
            Data = new() { ["message"] = message, ["downloadClient"] = t.Client.Name },
        });
        blocklist.Add(new BlocklistItem
        {
            TitleId = t.TitleId.Value,
            IssueId = t.IssueIds.FirstOrDefault() is var i and > 0 ? i : null,
            SourceTitle = t.SourceTitle,
            Protocol = t.Protocol,
            Indexer = t.Indexer,
            Message = message,
        });
    }

    public async Task RemoveFromQueue(string key, bool removeFromClient, bool addToBlocklist, CancellationToken ct)
    {
        var t = tracked.Find(key) ?? throw new KeyNotFoundException("That download is no longer in the queue");
        if (addToBlocklist && t.TitleId != null)
        {
            blocklist.Add(new BlocklistItem
            {
                TitleId = t.TitleId.Value,
                IssueId = t.IssueIds.FirstOrDefault() is var i and > 0 ? i : null,
                SourceTitle = t.SourceTitle,
                Protocol = t.Protocol,
                Indexer = t.Indexer,
                Message = "Removed from queue and blocklisted by user",
            });
        }

        if (removeFromClient && Client(t) is { } client)
        {
            await client.Remove(t.Client, t.Item.DownloadId, deleteData: true, ct);
        }

        // Make sure it's not imported later if it stays in the client.
        if (!removeFromClient && t.TitleId != null)
        {
            history.Add(new HistoryItem
            {
                TitleId = t.TitleId.Value,
                EventType = HistoryEventType.DownloadFailed,
                SourceTitle = t.SourceTitle,
                DownloadId = t.Item.DownloadId,
                Data = new() { ["message"] = "Removed from queue by user" },
            });
        }

        tracked.Forget(t);
    }

    private IDownloadClient? Client(TrackedDownload t) => clientImpls.FirstOrDefault(c => c.Implementation == t.Client.Implementation);
}
