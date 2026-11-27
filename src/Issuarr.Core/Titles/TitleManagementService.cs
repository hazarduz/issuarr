using Issuarr.Core.History;
using Issuarr.Core.Issues;
using Issuarr.Core.Organizer;

namespace Issuarr.Core.Titles;

public enum MonitorOption
{
    All,
    Future,
    Missing,
    None,
}

public class AddTitleOptions
{
    public MonitorOption Monitor { get; set; } = MonitorOption.All;
    public bool SearchForMissing { get; set; }
    public DateOnly? FirstIssueDate { get; set; }

    /// <summary>eBooks: add/monitor just this one book of the author (by metadata id, falling back to its title).</summary>
    public string? BookExternalId { get; set; }

    public string? BookTitle { get; set; }
}

public record RenamePreview(int IssueFileId, int IssueId, string Issue, string ExistingPath, string NewPath);

public interface ITitleManagementService
{
    Task<Title> Add(Title title, string rootFolder, AddTitleOptions options, CancellationToken ct);
    void Delete(int titleId, bool deleteFiles);
    List<RenamePreview> PreviewRename(int titleId);
    int Rename(int titleId, IReadOnlyCollection<int>? fileIds);
    void DeleteIssueFile(int fileId);

    /// <summary>
    /// Links a title (e.g. one created by Library Import) to a series on ComicVine/Metron, then
    /// refreshes it. Existing issues are matched by number so their files are kept.
    /// </summary>
    Task<Title> Match(int titleId, string source, string externalId, bool useMetadataName, CancellationToken ct);

    void Unmatch(int titleId);

    Issue? FindBook(int titleId, string? externalId, string? bookTitle);
}

public class TitleManagementService(
    ITitleService titles,
    ITitleRepository titleRepo,
    IEnumerable<Metadata.IMetadataProvider> metadata,
    IRefreshTitleService refresh,
    IIssueRepository issues,
    IIssueFileRepository files,
    IHistoryService history,
    FileNameBuilder names,
    IClock clock,
    MediaFiles.IDiskScanService? diskScan = null) : ITitleManagementService
{
    public async Task<Title> Add(Title title, string rootFolder, AddTitleOptions options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title.Path))
        {
            // Use a folder that's already there ("Retro Gamer", "Retro Gamer (2004)") before making a new one.
            title.Path = ExistingFolder(rootFolder, title.Name) ?? Path.Combine(rootFolder, names.TitleFolder(title));
        }

        var existing = title.MetadataSource != null && title.ExternalId != null
            ? titles.All().FirstOrDefault(t => t.MetadataSource == title.MetadataSource && t.ExternalId == title.ExternalId)
            : null;
        if (existing != null)
        {
            if (options.BookExternalId == null && options.BookTitle == null)
            {
                throw new ArgumentException($"{title.Name} is already in your library");
            }

            // Adding another book by an author we already have: just monitor that book.
            await refresh.Refresh(existing.Id, ct);
            var book = FindBook(existing.Id, options.BookExternalId, options.BookTitle)
                       ?? throw new ArgumentException($"Couldn't find that book in {existing.Name}'s book list");
            issues.SetMonitored([book.Id], true);
            if (!existing.Monitored)
            {
                existing.Monitored = true;
                titleRepo.Update(existing);
            }

            return titles.Get(existing.Id)!;
        }

        var onlyOneBook = options.BookExternalId != null || options.BookTitle != null;
        title.Monitored = options.Monitor != MonitorOption.None || onlyOneBook;
        var added = titles.Add(title, rootFolder);
        Directory.CreateDirectory(added.Path);

        diskScan?.Scan(added.Id);
        try
        {
            await refresh.Refresh(added.Id, ct, options.FirstIssueDate);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Keep the title; the issue list can be refreshed later.
            throw new TitleAddedWithWarningException(added, $"Added, but couldn't load issues: {e.Message}");
        }

        diskScan?.Scan(added.Id); // again, in case the refresh added issues that files belong to
        ApplyMonitoring(added.Id, onlyOneBook ? MonitorOption.None : options.Monitor);
        if (onlyOneBook && FindBook(added.Id, options.BookExternalId, options.BookTitle) is { } chosen)
        {
            issues.SetMonitored([chosen.Id], true);
        }

        return titles.Get(added.Id)!;
    }

    /// <summary>A sub-folder of the root folder whose name is the title's name (optionally followed by a year).</summary>
    public static string? ExistingFolder(string rootFolder, string name)
    {
        if (!Directory.Exists(rootFolder))
        {
            return null;
        }

        var wanted = Parser.ReleaseParser.CleanTitle(name);
        return Directory.EnumerateDirectories(rootFolder)
            .FirstOrDefault(d => Parser.ReleaseParser.CleanTitle(LibraryImport.LibraryImportService.ParseFolderName(Path.GetFileName(d)).Name) == wanted);
    }

    public Issue? FindBook(int titleId, string? externalId, string? bookTitle)
    {
        var all = issues.GetByTitle(titleId);
        var key = bookTitle == null ? null : Parser.BookMatcher.Normalize(bookTitle);
        return all.FirstOrDefault(i => externalId != null && i.ExternalId == externalId)
               ?? (key == null ? null : all.FirstOrDefault(i => Parser.BookMatcher.Normalize(i.Number) == key))
               ?? (key == null ? null : all.FirstOrDefault(i => Parser.BookMatcher.Normalize(i.Number.Split(':')[0]) == Parser.BookMatcher.Normalize(bookTitle!.Split(':')[0])));
    }

    private void ApplyMonitoring(int titleId, MonitorOption option)
    {
        var all = issues.GetByTitle(titleId);
        var today = clock.Today.ToString("yyyy-MM-dd");
        var monitored = option switch
        {
            MonitorOption.All => all,
            MonitorOption.Future => all.Where(i => string.CompareOrdinal(i.ReleaseDate, today) > 0).ToList(),
            MonitorOption.Missing => all.Where(i => i.IssueFileId == null).ToList(),
            _ => [],
        };
        issues.SetMonitoredByTitle(titleId, false);
        if (monitored.Count > 0)
        {
            issues.SetMonitored(monitored.Select(i => i.Id), true);
        }
    }

    public async Task<Title> Match(int titleId, string source, string externalId, bool useMetadataName, CancellationToken ct)
    {
        var title = titleRepo.Get(titleId) ?? throw new KeyNotFoundException("Title not found");
        var provider = metadata.FirstOrDefault(m => m.Source == source) ?? throw new ArgumentException($"Unknown metadata source: {source}");
        var other = titleRepo.All().FirstOrDefault(t => t.Id != titleId && t.MetadataSource == source && t.ExternalId == externalId);
        if (other != null)
        {
            throw new ArgumentException($"That series is already matched to \"{other.Name}\" in your library");
        }

        // Look the series up first so a bad id fails before anything changes.
        var series = await provider.GetSeries(externalId, ct);

        // Switching to a different series: forget the old series' issue ids so numbers are re-matched.
        if (title.ExternalId != null && (title.ExternalId != externalId || title.MetadataSource != source))
        {
            foreach (var issue in issues.GetByTitle(titleId).Where(i => i.ExternalId != null))
            {
                issue.ExternalId = null;
                issues.Update(issue);
            }
        }

        title.MetadataSource = source;
        title.ExternalId = externalId;
        if (useMetadataName && !string.IsNullOrWhiteSpace(series.Name))
        {
            title.Name = series.Name;
            title.SortName = TitleService.SortName(series.Name);
        }

        titleRepo.Update(title);

        try
        {
            await refresh.Refresh(titleId, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new TitleAddedWithWarningException(titleRepo.Get(titleId)!, $"Matched, but couldn't load issues: {e.Message}");
        }

        return titleRepo.Get(titleId)!;
    }

    public void Unmatch(int titleId)
    {
        var title = titleRepo.Get(titleId) ?? throw new KeyNotFoundException("Title not found");
        title.MetadataSource = null;
        title.ExternalId = null;
        titleRepo.Update(title);
        foreach (var issue in issues.GetByTitle(titleId).Where(i => i.ExternalId != null))
        {
            issue.ExternalId = null;
            issues.Update(issue);
        }
    }

    public void Delete(int titleId, bool deleteFiles)
    {
        var title = titles.Get(titleId) ?? throw new KeyNotFoundException("Title not found");
        if (deleteFiles)
        {
            // Only delete files Issuarr knows about, then the folder if that leaves it empty.
            foreach (var f in files.GetByTitle(titleId))
            {
                var p = Path.Combine(title.Path, f.RelativePath);
                if (File.Exists(p))
                {
                    File.Delete(p);
                }
            }

            DeleteEmptyFolders(title.Path);
        }

        titles.Delete(titleId);
    }

    public List<RenamePreview> PreviewRename(int titleId)
    {
        var title = titles.Get(titleId) ?? throw new KeyNotFoundException("Title not found");
        var fileMap = files.GetByTitle(titleId).ToDictionary(f => f.Id);
        var result = new List<RenamePreview>();
        foreach (var issue in issues.GetByTitle(titleId).Where(i => i.IssueFileId != null))
        {
            if (!fileMap.TryGetValue(issue.IssueFileId!.Value, out var f))
            {
                continue;
            }

            var newName = names.IssueFileName(title, issue, Path.GetExtension(f.RelativePath));
            var newRel = newName; // Renamed files go straight into the title folder.
            if (!string.Equals(newRel, f.RelativePath, StringComparison.Ordinal))
            {
                result.Add(new RenamePreview(f.Id, issue.Id, issue.Number, f.RelativePath, newRel));
            }
        }

        return result.OrderBy(r => r.NewPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public int Rename(int titleId, IReadOnlyCollection<int>? fileIds)
    {
        var title = titles.Get(titleId)!;
        var renamed = 0;
        foreach (var p in PreviewRename(titleId).Where(p => fileIds == null || fileIds.Contains(p.IssueFileId)))
        {
            var from = Path.Combine(title.Path, p.ExistingPath);
            var to = Path.Combine(title.Path, p.NewPath);
            if (!File.Exists(from) || File.Exists(to))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Move(from, to);
            files.UpdatePath(p.IssueFileId, p.NewPath);
            history.Add(new HistoryItem
            {
                TitleId = titleId,
                IssueId = p.IssueId,
                EventType = HistoryEventType.FileRenamed,
                SourceTitle = p.ExistingPath,
                Data = new() { ["path"] = p.NewPath },
            });
            renamed++;
        }

        DeleteEmptyFolders(title.Path, keepRoot: true);
        return renamed;
    }

    public void DeleteIssueFile(int fileId)
    {
        var file = files.Get(fileId) ?? throw new KeyNotFoundException("File not found");
        var title = titles.Get(file.TitleId)!;
        var path = Path.Combine(title.Path, file.RelativePath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var issue = issues.GetByTitle(title.Id).FirstOrDefault(i => i.IssueFileId == fileId);
        issues.ClearFile(fileId);
        files.Delete(fileId);
        history.Add(new HistoryItem
        {
            TitleId = title.Id,
            IssueId = issue?.Id,
            EventType = HistoryEventType.FileDeleted,
            SourceTitle = file.RelativePath,
            Data = new() { ["reason"] = "Deleted by user" },
        });
    }

    private static void DeleteEmptyFolders(string path, bool keepRoot = false)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var sub in Directory.GetDirectories(path))
        {
            DeleteEmptyFolders(sub);
        }

        if (!keepRoot && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);
        }
    }
}

public class TitleAddedWithWarningException(Title title, string message) : Exception(message)
{
    public Title Title { get; } = title;
}
