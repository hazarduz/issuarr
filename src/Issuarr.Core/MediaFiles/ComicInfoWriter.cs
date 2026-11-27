using System.IO.Compression;
using System.Xml.Linq;
using Issuarr.Core.Issues;
using Issuarr.Core.Titles;

namespace Issuarr.Core.MediaFiles;

/// <summary>
/// Adds a ComicInfo.xml to CBZ files so Komga, Kavita and other readers know the
/// series, number and date. Existing ComicInfo.xml files are left alone.
/// </summary>
public static class ComicInfoWriter
{
    public static bool TryWrite(string cbzPath, Title title, Issue issue)
    {
        try
        {
            using var zip = ZipFile.Open(cbzPath, ZipArchiveMode.Update);
            if (zip.Entries.Any(e => e.FullName.Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var entry = zip.CreateEntry("ComicInfo.xml", CompressionLevel.Optimal);
            using var stream = entry.Open();
            Build(title, issue).Save(stream);
            return true;
        }
        catch (InvalidDataException)
        {
            return false; // Not actually a zip (some "cbz" files are rar); leave it as is.
        }
    }

    public static XDocument Build(Title title, Issue issue)
    {
        var root = new XElement("ComicInfo",
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            new XElement("Series", title.Name));

        var number = IssueNumbering.GetNumber(issue.Number);
        root.Add(new XElement("Number", number?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? issue.Number));
        if (!string.IsNullOrEmpty(issue.IssueTitle))
        {
            root.Add(new XElement("Title", issue.IssueTitle));
        }

        root.Add(new XElement("Volume", title.Year));
        if (DateOnly.TryParse(issue.ReleaseDate, out var date))
        {
            root.Add(new XElement("Year", date.Year), new XElement("Month", date.Month), new XElement("Day", date.Day));
        }

        if (!string.IsNullOrEmpty(title.Publisher))
        {
            root.Add(new XElement("Publisher", title.Publisher));
        }

        if (!string.IsNullOrEmpty(title.Overview))
        {
            root.Add(new XElement("Summary", title.Overview));
        }

        if (title.Kind == TitleKind.Magazine)
        {
            root.Add(new XElement("Format", "Magazine"));
        }

        root.Add(new XElement("Notes", "Tagged by Issuarr"));
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }
}
