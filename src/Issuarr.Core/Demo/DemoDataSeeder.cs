using Issuarr.Core.Issues;
using Issuarr.Core.Titles;

namespace Issuarr.Core.Demo;

/// <summary>
/// Fills an empty library with sample titles so the UI can be explored
/// before any real indexers or download clients are set up.
/// Enabled with ISSUARR_DEMO=true.
/// </summary>
public class DemoDataSeeder(ITitleService titles, IIssueRepository issues, IIssueFileRepository files, IClock clock)
{
    private record Seed(
        string Name, TitleKind Kind, string Publisher, int Year, string Frequency, int CadenceDays, int Count,
        string[] Palette, string Overview, string[] Tags, bool Ended = false, bool Monitored = true, string? Profile = null);

    private static readonly Seed[] Seeds =
    [
        new("Starfall Rangers", TitleKind.Comic, "Northlight Comics", 2023, "Monthly", 30, 38, ["#3a1c71", "#d76d77"],
            "A disgraced orbital pilot and a crew of misfit salvagers chase a signal no one else can hear across a collapsing star system.", ["sci-fi", "ongoing"]),
        new("The Hollow Crown", TitleKind.Comic, "Gravel & Ink", 2021, "Monthly", 30, 24, ["#1f1c2c", "#928dab"],
            "A gothic fantasy about a kingdom ruled by an empty throne and the court that pretends otherwise.", ["fantasy"], Ended: true),
        new("Circuit & Solder", TitleKind.Magazine, "Workbench Media", 2015, "Monthly", 30, 30, ["#0f2027", "#2c5364"],
            "Hands-on electronics projects, kit reviews and repair guides for makers of every level.", ["tech", "diy"], Profile: "PDF Preferred"),
        new("Trail Weekly", TitleKind.Magazine, "Open Country Press", 2019, "Weekly", 7, 52, ["#134e5e", "#71b280"],
            "Routes, gear tests and stories from the hiking and trail running community, every week.", ["outdoors"], Profile: "PDF Preferred"),
        new("Neon Alley", TitleKind.Comic, "Northlight Comics", 2025, "Monthly", 30, 14, ["#fc466b", "#3f5efb"],
            "A cyberpunk noir following a courier who delivers memories instead of packages.", ["sci-fi", "noir"]),
        new("The Kitchen Table", TitleKind.Magazine, "Hearth Publishing", 2012, "Bi-monthly", 60, 18, ["#f12711", "#f5af19"],
            "Seasonal home cooking, baking and the stories behind family recipes.", ["food"], Profile: "PDF Preferred"),
        new("Moth & Lantern", TitleKind.Comic, "Small Hours", 2024, "Monthly", 30, 22, ["#232526", "#414345"],
            "A quiet horror anthology. Every issue, a new town and a new light that should not be followed.", ["horror", "anthology"], Monitored: false),
        new("Pixel Quarterly", TitleKind.Magazine, "Respawn Media", 2018, "Quarterly", 91, 16, ["#8e2de2", "#4a00e0"],
            "Long-form features on game design, preservation and the people who make games.", ["games"]),
        new("Iron Tide", TitleKind.Comic, "Gravel & Ink", 2022, "Monthly", 30, 46, ["#2c3e50", "#4ca1af"],
            "Naval war epic set on an ocean world where the tides are controlled by a single empire.", ["war", "ongoing"]),
        new("Field Notes Monthly", TitleKind.Magazine, "Open Country Press", 2010, "Monthly", 30, 26, ["#56ab2f", "#a8e063"],
            "Birding, botany and backyard science for curious naturalists.", ["nature"], Profile: "PDF Preferred"),
        new("Paper Ghosts", TitleKind.Comic, "Small Hours", 2020, "Monthly", 30, 12, ["#e0eafc", "#9aa9c4"],
            "Two sisters inherit a print shop haunted by every story it ever published.", ["mystery"], Ended: true),
        new("Velocity", TitleKind.Magazine, "Apex Motor Group", 2016, "Monthly", 30, 28, ["#cb2d3e", "#ef473a"],
            "Track tests, restoration builds and motorsport coverage.", ["cars"]),
    ];

    private static readonly string[] ChapterNames =
        ["First Light", "The Long Dark", "Undertow", "Signal Lost", "Ashes", "Homecoming", "Fault Lines", "Paper Moon", "The Turning", "Low Tide"];

    public bool SeedIfEmpty()
    {
        if (titles.All().Count > 0)
        {
            return false;
        }

        var today = clock.Today;
        for (var idx = 0; idx < Seeds.Length; idx++)
        {
            var s = Seeds[idx];
            var rng = new Random(idx * 7919 + 13);
            var root = s.Kind == TitleKind.Comic ? "/data/comics" : "/data/magazines";
            var title = titles.Add(new Title
            {
                Name = s.Name,
                Kind = s.Kind,
                Publisher = s.Publisher,
                Year = s.Year,
                Frequency = s.Frequency,
                Status = s.Ended ? TitleStatus.Ended : TitleStatus.Continuing,
                Monitored = s.Monitored,
                QualityProfile = s.Profile ?? (s.Kind == TitleKind.Comic ? "CBZ / CBR" : "Any"),
                Overview = s.Overview,
                Tags = [.. s.Tags],
                Palette = [.. s.Palette],
            }, root);

            var lastOffset = s.Ended ? -200 : (int)Math.Round(s.CadenceDays * (0.3 + rng.NextDouble()));
            var list = new List<(Issue Issue, bool HasFile)>();
            for (var i = 0; i < s.Count; i++)
            {
                var fromEnd = s.Count - 1 - i;
                var date = today.AddDays(lastOffset - fromEnd * s.CadenceDays);
                var future = date > today;
                var roll = rng.NextDouble();
                var hasFile = !future && (s.Monitored ? roll < 0.82 : roll < 0.4);

                list.Add((new Issue
                {
                    TitleId = title.Id,
                    Number = IssueNumbering.Format(s.Kind, s.Frequency, i + 1, date),
                    SortKey = s.Kind == TitleKind.Comic ? i + 1 : date.Year * 10000 + date.Month * 100 + date.Day,
                    IssueTitle = s.Kind == TitleKind.Comic && rng.NextDouble() < 0.35 ? ChapterNames[rng.Next(ChapterNames.Length)] : null,
                    ReleaseDate = date.ToString("yyyy-MM-dd"),
                    Monitored = s.Monitored,
                }, hasFile));
            }

            foreach (var (issue, hasFile) in list.Where(x => x.HasFile))
            {
                var ext = s.Kind == TitleKind.Comic ? "cbz" : "pdf";
                var mb = s.Kind == TitleKind.Comic ? 28 + rng.NextDouble() * 60 : 40 + rng.NextDouble() * 120;
                var file = files.Insert(new IssueFile
                {
                    TitleId = title.Id,
                    RelativePath = $"{s.Name} {issue.Number.TrimStart('#')}.{ext}",
                    Size = (long)(mb * 1024 * 1024),
                    Format = ext.ToUpperInvariant(),
                    DateAdded = clock.UtcNow.ToString("O"),
                });
                issue.IssueFileId = file.Id;
            }

            issues.InsertMany(list.Select(x => x.Issue));
        }

        return true;
    }
}
