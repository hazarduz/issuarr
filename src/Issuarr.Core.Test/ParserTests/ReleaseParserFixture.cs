using Issuarr.Core.Parser;

namespace Issuarr.Core.Test.ParserTests;

[TestFixture]
public class ReleaseParserFixture
{
    [TestCase("Starfall.Rangers.037.2026.Digital.cbz", "Starfall Rangers", 37, 2026, "CBZ")]
    [TestCase("Iron Tide #46 (2026) (Webrip) (Zone-Empire).cbr", "Iron Tide", 46, 2026, "CBR")]
    [TestCase("Iron Tide 046 (2026) (digital) (Zone-Empire)", "Iron Tide", 46, 2026, null)]
    [TestCase("Neon.Alley.013.2026.cbz", "Neon Alley", 13, 2026, "CBZ")]
    [TestCase("The Hollow Crown 012.5 (2022)", "The Hollow Crown", 12.5, 2022, null)]
    [TestCase("Moth & Lantern #1 (2024)", "Moth & Lantern", 1, 2024, null)]
    [TestCase("2000 AD 2412 (2024) (digital)", "2000 AD", 2412, 2024, null)]
    [TestCase("Velocity Issue 112 2026 PDF", "Velocity", 112, 2026, "PDF")]
    public void should_parse_comic_style_releases(string release, string title, decimal issue, int year, string? format)
    {
        var result = ReleaseParser.Parse(release);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.TitleName, Is.EqualTo(title));
            Assert.That(result.IssueNumber, Is.EqualTo(issue));
            Assert.That(result.Year, Is.EqualTo(year));
            Assert.That(result.Format, Is.EqualTo(format));
            Assert.That(result.IsDated, Is.False);
        });
    }

    [TestCase("Circuit.and.Solder.November.2026.PDF", "Circuit and Solder", 2026, 11)]
    [TestCase("Field Notes Monthly - December 2026.pdf", "Field Notes Monthly", 2026, 12)]
    [TestCase("Velocity - 2026-11.pdf", "Velocity", 2026, 11)]
    [TestCase("The.Kitchen.Table.Sept.2026.True.PDF-MAGS", "The Kitchen Table", 2026, 9)]
    [TestCase("Velocity UK - Jan 2027", "Velocity UK", 2027, 1)]
    public void should_parse_monthly_magazines(string release, string title, int year, int month)
    {
        var result = ReleaseParser.Parse(release)!;

        Assert.Multiple(() =>
        {
            Assert.That(result.TitleName, Is.EqualTo(title));
            Assert.That(result.Year, Is.EqualTo(year));
            Assert.That(result.Month, Is.EqualTo(month));
            Assert.That(result.IssueNumber, Is.Null);
        });
    }

    [Test]
    public void should_parse_weekly_magazine()
    {
        var result = ReleaseParser.Parse("Trail.Weekly.2026.W48.True.PDF")!;

        Assert.Multiple(() =>
        {
            Assert.That(result.TitleName, Is.EqualTo("Trail Weekly"));
            Assert.That(result.Year, Is.EqualTo(2026));
            Assert.That(result.Week, Is.EqualTo(48));
            Assert.That(result.IsTruePdf, Is.True);
            Assert.That(result.Format, Is.EqualTo("PDF"));
        });
    }

    [Test]
    public void should_parse_quarterly_magazine()
    {
        var result = ReleaseParser.Parse("Pixel Quarterly Q3 2026")!;

        Assert.That(result.TitleName, Is.EqualTo("Pixel Quarterly"));
        Assert.That(result.Quarter, Is.EqualTo(3));
        Assert.That(result.Year, Is.EqualTo(2026));
    }

    [Test]
    public void should_detect_release_group()
    {
        Assert.That(ReleaseParser.Parse("Iron Tide 046 (2026) (digital) (Zone-Empire).cbr")!.ReleaseGroup, Is.EqualTo("Zone-Empire"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("2026")]
    public void should_return_null_for_unparseable(string release)
    {
        Assert.That(ReleaseParser.Parse(release), Is.Null);
    }

    [TestCase("The Kitchen Table", "Kitchen.Table")]
    [TestCase("Circuit & Solder", "Circuit and Solder")]
    public void clean_titles_should_match(string a, string b)
    {
        Assert.That(ReleaseParser.CleanTitle(a), Is.EqualTo(ReleaseParser.CleanTitle(b)));
    }
}
