using System.Globalization;
using System.Text.RegularExpressions;
using Issuarr.Core.Issues;
using Issuarr.Core.Parser;
using Issuarr.Core.Titles;

namespace Issuarr.Core.LibraryImport;

/// <summary>A folder found while scanning, that could become a title.</summary>
public class ImportCandidate
{
    public string Path { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string Name { get; set; } = "";
    public int? Year { get; set; }
    public TitleKind Kind { get; set; }
    public int FileCount { get; set; }
    public int MatchedFileCount { get; set; }
    public long Size { get; set; }
    public int? ExistingTitleId { get; set; }
    public string? ExistingTitleName { get; set; }
    public List<string> SampleFiles { get; set; } = [];
}

public class ImportRequest
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public int? Year { get; set; }
    public TitleKind Kind { get; set; }
    public string? Publisher { get; set; }
    public string? MetadataSource { get; set; }
    public string? ExternalId { get; set; }
    public string? CoverUrl { get; set; }
}

public class ImportResult
{
    public string Path { get; set; } = "";
    public int? TitleId { get; set; }
    public string? Slug { get; set; }
    public int IssuesImported { get; set; }
    public List<string> UnmatchedFiles { get; set; } = [];
    public string? Error { get; set; }
    public string? Warning { get; set; }
}

public interface ILibraryImportService
{
    List<ImportCandidate> Scan(string path);
    List<ImportResult> Import(IEnumerable<ImportRequest> requests);
}

/// <summary>
/// Turns an existing collection on disk into titles and issues: one title per
/// folder, one issue per recognisable file (CBZ, CBR, CB7, PDF, EPUB).
/// </summary>
public partial class LibraryImportService(ITitleService titles, IIssueRepository issues, IIssueFileRepository files, IClock clock)
    : ILibraryImportService
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".cbz", ".cbr", ".cb7", ".pdf", ".epub", ".mobi", ".azw3", ".azw" };
    private static readonly HashSet<string> EbookExtensions = new(StringComparer.OrdinalIgnoreCase) { ".epub", ".mobi", ".azw3", ".azw" };

    private static readonly string[][] Palettes =
    [
        ["#3a1c71", "#d76d77"], ["#0f2027", "#2c5364"], ["#134e5e", "#71b280"], ["#fc466b", "#3f5efb"],
        ["#f12711", "#f5af19"], ["#8e2de2", "#4a00e0"], ["#2c3e50", "#4ca1af"], ["#56ab2f", "#a8e063"],
        ["#cb2d3e", "#ef473a"], ["#0b486b", "#f56217"], ["#2b5876", "#4e4376"], ["#ee0979", "#ff6a00"],
    ];

    public List<ImportCandidate> Scan(string path)
    {
        var root = new DirectoryInfo(path);
        if (!root.Exists)
        {
            throw new DirectoryNotFoundException($"Folder not found: {path}");
        }

        var existing = titles.All();
        var candidates = new List<ImportCandidate>();

        // Each sub-folder is a title. If the chosen folder holds comic/magazine files itself,
        // it's treated as a single title too (someone picked one series' folder directly).
        var folders = root.EnumerateDirectories().Where(d => !d.Name.StartsWith('.')).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (root.EnumerateFiles().Any(IsComicFile))
        {
            folders.Insert(0, root);
        }

        foreach (var dir in folders)
        {
            var found = dir == root
                ? dir.EnumerateFiles().Where(IsComicFile).ToList()
                : SafeFiles(dir);
            if (found.Count == 0)
            {
                continue;
            }

            var (name, year) = ParseFolderName(dir.Name);
            var parsed = found.Select(f => ReleaseParser.Parse(f.Name)).ToList();
            var kind = GuessKind(found, parsed);
            var match = existing.FirstOrDefault(t => SamePath(t.Path, dir.FullName))
                        ?? existing.FirstOrDefault(t => ReleaseParser.CleanTitle(t.Name) == ReleaseParser.CleanTitle(name) && (year == null || t.Year == year));

            candidates.Add(new ImportCandidate
            {
                Path = dir.FullName,
                FolderName = dir.Name,
                Name = name,
                Year = year ?? parsed.Where(p => p?.Year != null).Select(p => p!.Year).Min(),
                Kind = kind,
                FileCount = found.Count,
                MatchedFileCount = kind == TitleKind.Book
                    ? found.Select(f => Path.GetFileNameWithoutExtension(f.Name).ToLowerInvariant()).Distinct().Count()
                    : parsed.Count(p => p != null && (p.IssueNumber != null || (p.IsDated && p.Year != null))),
                Size = found.Sum(f => f.Length),
                ExistingTitleId = match?.Id,
                ExistingTitleName = match?.Name,
                SampleFiles = found.Take(3).Select(f => f.Name).ToList(),
            });
        }

        return candidates;
    }

    public List<ImportResult> Import(IEnumerable<ImportRequest> requests)
    {
        var results = new List<ImportResult>();
        foreach (var req in requests)
        {
            var result = new ImportResult { Path = req.Path };
            results.Add(result);
            try
            {
                ImportOne(req, result);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                result.Error = e.Message;
            }
        }

        return results;
    }

    private void ImportOne(ImportRequest req, ImportResult result)
    {
        var dir = new DirectoryInfo(req.Path);
        if (!dir.Exists)
        {
            throw new ArgumentException($"Folder not found: {req.Path}");
        }

        var all = titles.All();
        if (all.Any(t => SamePath(t.Path, dir.FullName)))
        {
            throw new ArgumentException("This folder is already in your library");
        }

        if (!string.IsNullOrEmpty(req.ExternalId) && all.FirstOrDefault(t => t.MetadataSource == req.MetadataSource && t.ExternalId == req.ExternalId) is { } dup)
        {
            throw new ArgumentException($"That series is already in your library as \"{dup.Name}\"");
        }

        var name = string.IsNullOrWhiteSpace(req.Name) ? ParseFolderName(dir.Name).Name : req.Name.Trim();
        var found = SafeFiles(dir);
        var drafts = new List<(Issue Issue, FileInfo File, string Freq)>();

        foreach (var file in found.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var draft = req.Kind == TitleKind.Book ? BookIssue(file, name, drafts.Count) : ToIssue(file, ReleaseParser.Parse(file.Name), req.Year);
            if (draft == null)
            {
                result.UnmatchedFiles.Add(System.IO.Path.GetRelativePath(dir.FullName, file.FullName));
                continue;
            }

            // Two files for the same issue (e.g. CBR and CBZ): keep the first, report the rest.
            if (drafts.Any(d => d.Issue.Number == draft.Value.Issue.Number))
            {
                result.UnmatchedFiles.Add(System.IO.Path.GetRelativePath(dir.FullName, file.FullName));
                continue;
            }

            drafts.Add((draft.Value.Issue, file, draft.Value.Freq));
        }

        var year = req.Year ?? drafts.Select(d => int.Parse(d.Issue.ReleaseDate[..4])).DefaultIfEmpty(clock.Today.Year).Min();
        var frequency = drafts.Count == 0
            ? (req.Kind == TitleKind.Comic ? "Monthly" : null)
            : drafts.GroupBy(d => d.Freq).OrderByDescending(g => g.Count()).First().Key;

        var title = titles.Add(new Title
        {
            Name = name,
            Kind = req.Kind,
            Publisher = string.IsNullOrWhiteSpace(req.Publisher) ? null : req.Publisher.Trim(),
            Year = year,
            Frequency = frequency,
            Status = TitleStatus.Continuing,
            Monitored = true,
            QualityProfile = req.Kind switch { TitleKind.Comic => "CBZ / CBR", TitleKind.Book => "eBook", _ => "Any" },
            Path = dir.FullName,
            MetadataSource = string.IsNullOrEmpty(req.ExternalId) ? null : req.MetadataSource,
            ExternalId = string.IsNullOrEmpty(req.ExternalId) ? null : req.ExternalId,
            CoverUrl = req.CoverUrl,
            Palette = [.. Palettes[(int)((uint)ReleaseParser.CleanTitle(name).GetHashCode(StringComparison.Ordinal) % Palettes.Length)]],
        }, "");

        foreach (var (issue, file, _) in drafts)
        {
            var stored = files.Insert(new IssueFile
            {
                TitleId = title.Id,
                RelativePath = System.IO.Path.GetRelativePath(dir.FullName, file.FullName),
                Size = file.Length,
                Format = file.Extension.TrimStart('.').ToUpperInvariant(),
                DateAdded = clock.UtcNow.ToString("O"),
            });
            issue.TitleId = title.Id;
            issue.IssueFileId = stored.Id;
        }

        issues.InsertMany(drafts.Select(d => d.Issue));
        result.TitleId = title.Id;
        result.Slug = title.Slug;
        result.IssuesImported = drafts.Count;
    }

    /// <summary>Builds an issue from a file name, or null when we can't tell which issue it is.</summary>
    public static (Issue Issue, string Freq)? ToIssue(FileInfo file, ParsedRelease? parsed, int? titleYear)
    {
        if (parsed == null)
        {
            return null;
        }

        if (parsed.IsDated && parsed.Year is { } y)
        {
            DateOnly date;
            string freq;
            if (parsed.Week is { } w)
            {
                date = DateOnly.FromDateTime(ISOWeek.ToDateTime(y, Math.Clamp(w, 1, ISOWeek.GetWeeksInYear(y)), DayOfWeek.Monday));
                freq = "Weekly";
            }
            else if (parsed.Quarter is { } q)
            {
                date = new DateOnly(y, (q - 1) * 3 + 1, 1);
                freq = "Quarterly";
            }
            else
            {
                date = new DateOnly(y, parsed.Month!.Value, 1);
                freq = "Monthly";
            }

            return (new Issue
            {
                Number = IssueNumbering.Format(TitleKind.Magazine, freq, 0, date),
                SortKey = date.Year * 10000 + date.Month * 100 + date.Day,
                ReleaseDate = date.ToString("yyyy-MM-dd"),
                Monitored = true,
            }, freq);
        }

        if (parsed.IssueNumber is { } n)
        {
            // We rarely know a comic's exact release date from its file; the year (from the file
            // or the folder) is the best guess, otherwise when the file was last written.
            var modified = DateOnly.FromDateTime(file.LastWriteTime);
            var year = parsed.Year ?? titleYear;
            var date = year == null ? modified : modified.Year == year ? modified : new DateOnly(year.Value, 1, 1);

            return (new Issue
            {
                Number = "#" + n.ToString("0.##", CultureInfo.InvariantCulture),
                SortKey = (double)n,
                ReleaseDate = date.ToString("yyyy-MM-dd"),
                Monitored = true,
            }, "Monthly");
        }

        return null;
    }

    /// <summary>
    /// A book file: "Author - Title (2010).epub", "Title - Author.epub" or just "Title.epub".
    /// The author's name and any year/format tags are stripped to leave the book title.
    /// </summary>
    public static (Issue Issue, string Freq)? BookIssue(FileInfo file, string author, int index)
    {
        var name = Path.GetFileNameWithoutExtension(file.Name).Replace('_', ' ');
        name = Regex.Replace(name, @"[\(\[][^\)\]]*[\)\]]", " ");
        var parts = name.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (parts.Count > 1)
        {
            parts.RemoveAll(p => BookMatcher.AuthorScore(BookMatcher.Normalize(p), author) > 0 && BookMatcher.Normalize(p).Trim().Split(' ').Length <= 4);
        }

        var title = Regex.Replace(string.Join(" - ", parts), @"\s{2,}", " ").Trim(' ', '-', '.');
        if (title.Length == 0)
        {
            return null;
        }

        var year = Regex.Match(file.Name, @"\b(19|20)\d{2}\b");
        var date = year.Success ? $"{year.Value}-01-01" : DateOnly.FromDateTime(file.LastWriteTime).ToString("yyyy-MM-dd");
        return (new Issue { Number = title, SortKey = int.Parse(date[..4]) * 10000 + index / 1000.0, ReleaseDate = date, Monitored = true }, "");
    }

    public static (string Name, int? Year) ParseFolderName(string folder)
    {
        var m = FolderYear().Match(folder);
        if (m.Success)
        {
            return (m.Groups["name"].Value.Trim(' ', '-', '_', '.'), int.Parse(m.Groups["year"].Value));
        }

        return (folder.Replace('_', ' ').Replace('.', ' ').Trim(), null);
    }

    private static TitleKind GuessKind(List<FileInfo> found, List<ParsedRelease?> parsed)
    {
        if (found.Count(f => EbookExtensions.Contains(f.Extension)) * 2 > found.Count)
        {
            return TitleKind.Book;
        }

        var comicFiles = found.Count(f => f.Extension.StartsWith(".cb", StringComparison.OrdinalIgnoreCase));
        if (comicFiles * 2 > found.Count)
        {
            return TitleKind.Comic;
        }

        // Folders of PDFs are almost always magazines, whether named by date or by issue number.
        if (found.Count(f => f.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) * 2 > found.Count)
        {
            return TitleKind.Magazine;
        }

        return parsed.Count(p => p?.IsDated == true) * 2 >= found.Count ? TitleKind.Magazine : TitleKind.Comic;
    }

    private static bool IsComicFile(FileInfo f) => Extensions.Contains(f.Extension) && !f.Name.StartsWith('.');

    private static List<FileInfo> SafeFiles(DirectoryInfo dir)
    {
        try
        {
            return dir.EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(IsComicFile)
                .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a.TrimEnd('/', '\\'), b.TrimEnd('/', '\\'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // "Iron Tide (2022)", "Iron Tide [2022]", "Iron Tide 2022"
    [GeneratedRegex(@"^(?<name>.+?)[\s._-]*[\(\[]?(?<year>(?:19|20)\d{2})[\)\]]?$")]
    private static partial Regex FolderYear();
}
