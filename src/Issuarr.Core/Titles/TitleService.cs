using System.Text.RegularExpressions;
using Issuarr.Core.Issues;
using Issuarr.Core.Parser;

namespace Issuarr.Core.Titles;

public interface ITitleService
{
    List<Title> All();
    Title? Get(int id);
    Title? FindBySlug(string slug);
    Title? FindByReleaseTitle(string parsedTitle);
    Title Add(Title title, string rootFolder);
    Title Update(Title title);
    void Delete(int id);
}

public partial class TitleService(ITitleRepository repo, IIssueRepository issues, IClock clock) : ITitleService
{
    public List<Title> All() => repo.All();

    public Title? Get(int id) => repo.Get(id);

    public Title? FindBySlug(string slug) => repo.FindBySlug(slug);

    public Title? FindByReleaseTitle(string parsedTitle)
    {
        var clean = ReleaseParser.CleanTitle(parsedTitle);
        return repo.All().FirstOrDefault(t => ReleaseParser.CleanTitle(t.Name) == clean);
    }

    public Title Add(Title title, string rootFolder)
    {
        if (string.IsNullOrWhiteSpace(title.Name))
        {
            throw new ArgumentException("Title name is required");
        }

        title.Name = title.Name.Trim();
        title.SortName = SortName(title.Name);
        title.Slug = UniqueSlug(title.Name);
        title.Added = clock.UtcNow.ToString("O");
        if (string.IsNullOrWhiteSpace(title.Path))
        {
            title.Path = Path.Combine(rootFolder, CleanFolderName($"{title.Name} ({title.Year})"));
        }

        return repo.Insert(title);
    }

    public Title Update(Title title)
    {
        var existing = repo.Get(title.Id) ?? throw new KeyNotFoundException($"Title {title.Id} not found");

        // Only user-editable fields; identity fields stay as they were.
        existing.Monitored = title.Monitored;
        existing.QualityProfile = title.QualityProfile;
        existing.Path = string.IsNullOrWhiteSpace(title.Path) ? existing.Path : title.Path;
        existing.Tags = title.Tags;
        existing.Frequency = title.Frequency;
        existing.Status = title.Status;
        repo.Update(existing);

        if (!existing.Monitored)
        {
            issues.SetMonitoredByTitle(existing.Id, false);
        }

        return existing;
    }

    public void Delete(int id) => repo.Delete(id);

    public static string SortName(string name) =>
        LeadingArticle().Replace(name.ToLowerInvariant(), "").Trim();

    public static string Slugify(string name) =>
        NonSlug().Replace(name.ToLowerInvariant().Replace("&", "and"), "-").Trim('-');

    public static string CleanFolderName(string name) =>
        InvalidPath().Replace(name, "").Replace(":", " -").Trim();

    private string UniqueSlug(string name)
    {
        var slug = Slugify(name);
        var candidate = slug;
        for (var i = 2; repo.FindBySlug(candidate) != null; i++)
        {
            candidate = $"{slug}-{i}";
        }

        return candidate;
    }

    [GeneratedRegex(@"^(the|a|an)\s+")]
    private static partial Regex LeadingArticle();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlug();

    [GeneratedRegex(@"[<>""/\\|?*]")]
    private static partial Regex InvalidPath();
}
