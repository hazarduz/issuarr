using Issuarr.Core.Decisions;
using Issuarr.Core.History;
using Issuarr.Core.Issues;
using Issuarr.Core.LibraryImport;
using Issuarr.Core.Parser;
using Issuarr.Core.Titles;
using Microsoft.Extensions.Logging;

namespace Issuarr.Core.MediaFiles;

public record DiskScanResult(bool FolderExists, int Added, int Removed, List<string> Unmatched)
{
    public string Summary =>
        !FolderExists ? "Folder not found on disk"
        : Added == 0 && Removed == 0 && Unmatched.Count == 0 ? "No file changes"
        : string.Join(", ", new[]
        {
            Added > 0 ? $"{Added} file{(Added == 1 ? "" : "s")} found" : null,
            Removed > 0 ? $"{Removed} missing file{(Removed == 1 ? "" : "s")} removed" : null,
            Unmatched.Count > 0 ? $"{Unmatched.Count} skipped (duplicates)" : null,
        }.Where(x => x != null));
}

public interface IDiskScanService
{
    /// <summary>
    /// Brings the database in line with the title's folder: picks up files that aren't tracked
    /// yet (matching them to issues, or creating issues for them) and forgets files that are gone.
    /// </summary>
    DiskScanResult Scan(int titleId);
}

public class DiskScanService(
    ITitleService titles,
    IIssueRepository issues,
    IIssueFileRepository files,
    IHistoryService history,
    IClock clock,
    ILogger<DiskScanService> log) : IDiskScanService
{
    public DiskScanResult Scan(int titleId)
    {
        var title = titles.Get(titleId) ?? throw new KeyNotFoundException("Title not found");
        if (!Directory.Exists(title.Path))
        {
            return new DiskScanResult(false, 0, 0, []);
        }

        var tracked = files.GetByTitle(titleId);
        var removed = 0;
        foreach (var f in tracked.Where(f => !File.Exists(Path.Combine(title.Path, f.RelativePath))))
        {
            var issue = issues.GetByTitle(titleId).FirstOrDefault(i => i.IssueFileId == f.Id);
            issues.ClearFile(f.Id);
            files.Delete(f.Id);
            history.Add(new HistoryItem
            {
                TitleId = titleId,
                IssueId = issue?.Id,
                EventType = HistoryEventType.FileDeleted,
                SourceTitle = f.RelativePath,
                Data = new() { ["reason"] = "Missing from disk" },
            });
            removed++;
        }

        var known = tracked.Select(f => Path.GetFullPath(Path.Combine(title.Path, f.RelativePath))).ToHashSet(StringComparer.Ordinal);
        var onDisk = new DirectoryInfo(title.Path)
            .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Where(f => LibraryImportService.Extensions.Contains(f.Extension) && !f.Name.StartsWith('.') && !known.Contains(f.FullName))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var added = 0;
        var unmatched = new List<string>();
        var profile = QualityProfiles.Get(title.QualityProfile);
        foreach (var file in onDisk)
        {
            var all = issues.GetByTitle(titleId);
            var issue = FindIssue(title, file, all);
            if (issue == null)
            {
                // No existing issue for it: create one from the file name (like Library Import does).
                var draft = title.Kind == TitleKind.Book
                    ? LibraryImportService.BookIssue(file, title.Name, all.Count)
                    : LibraryImportService.ToIssue(file, ReleaseParser.Parse(file.Name), title.Year > 0 ? title.Year : null);
                if (draft == null)
                {
                    unmatched.Add(Path.GetRelativePath(title.Path, file.FullName));
                    continue;
                }

                var created = draft.Value.Issue;
                created.TitleId = titleId;
                created.Monitored = title.Monitored;
                issues.InsertMany([created]);
                issue = issues.GetByTitle(titleId).FirstOrDefault(i => i.Number == created.Number && i.IssueFileId == null);
                if (issue == null)
                {
                    unmatched.Add(Path.GetRelativePath(title.Path, file.FullName));
                    continue;
                }
            }

            var format = file.Extension.TrimStart('.').ToUpperInvariant();
            if (issue.IssueFileId is { } existingId)
            {
                // Two files for one issue: keep the one the profile prefers.
                var existing = files.Get(existingId);
                if (existing == null || profile.Rank(existing.Format) <= profile.Rank(format))
                {
                    unmatched.Add(Path.GetRelativePath(title.Path, file.FullName));
                    continue;
                }

                issues.ClearFile(existingId);
                files.Delete(existingId);
            }

            var stored = files.Insert(new IssueFile
            {
                TitleId = titleId,
                RelativePath = Path.GetRelativePath(title.Path, file.FullName),
                Size = file.Length,
                Format = format,
                DateAdded = clock.UtcNow.ToString("O"),
            });
            issues.SetFile(issue.Id, stored.Id);
            added++;
        }

        if (added > 0 || removed > 0)
        {
            log.LogInformation("Disk scan of {Title}: {Added} added, {Removed} removed", title.Name, added, removed);
        }

        return new DiskScanResult(true, added, removed, unmatched);
    }

    private static Issue? FindIssue(Title title, FileInfo file, List<Issue> all)
    {
        if (title.Kind == TitleKind.Book)
        {
            return BookMatcher.MatchBook(BookMatcher.Normalize(Path.GetFileNameWithoutExtension(file.Name)), all);
        }

        var parsed = ReleaseParser.Parse(file.Name);
        if (parsed != null && DecisionMaker.MatchIssue(parsed, all) is { } byName)
        {
            return byName;
        }

        // Folder-per-issue layouts: "Retro Gamer/2024/Issue 260/file.pdf".
        var parent = file.Directory?.Name;
        var fromFolder = parent == null ? null : ReleaseParser.Parse($"{title.Name} {parent}");
        return fromFolder != null && (fromFolder.IssueNumber != null || fromFolder.IsDated) ? DecisionMaker.MatchIssue(fromFolder, all) : null;
    }
}
