using System.Text;
using System.Xml.Linq;
using Issuarr.Core.Configuration;
using Issuarr.Core.Issues;
using Issuarr.Core.MediaFiles;
using Issuarr.Core.Reading;
using Issuarr.Core.Titles;
using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.Opds;

/// <summary>
/// OPDS 1.2 catalog for e-reader apps (KOReader, Panels, Chunky, KyBook, Moon+ Reader, Thorium…).
/// Comics also offer OPDS-PSE page streaming so readers can page through CBZ/CBR without downloading.
/// Auth: HTTP Basic with the Issuarr login, or ?apikey= (see AuthGate).
/// </summary>
[ApiController]
[Route("opds")]
public class OpdsController(
    ITitleService titles,
    IIssueRepository issues,
    IIssueFileRepository files,
    IReadingHomeService home,
    ICoverService covers,
    ConfigFile config) : ControllerBase
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Opds = "http://opds-spec.org/2010/catalog";
    private static readonly XNamespace Pse = "http://vaemendis.net/opds-pse/ns";
    private static readonly XNamespace Dc = "http://purl.org/dc/terms/";
    private const string Nav = "application/atom+xml;profile=opds-catalog;kind=navigation";
    private const string Acq = "application/atom+xml;profile=opds-catalog;kind=acquisition";
    private const int PageSize = 50;

    [HttpGet("")]
    public IActionResult Root()
    {
        var feed = Feed("root", config.InstanceName, Nav);
        feed.Add(NavEntry("reading", "Continue Reading", "Issues and books you've started, and what's next", "reading", Acq));
        feed.Add(NavEntry("recent", "Recently Added", "The newest files in your library", "recent", Acq));
        feed.Add(NavEntry("comics", "Comics", "Comic series", "kind/comic", Nav));
        feed.Add(NavEntry("magazines", "Magazines", "Magazines", "kind/magazine", Nav));
        feed.Add(NavEntry("books", "eBooks", "Authors and their books", "kind/book", Nav));
        feed.Add(NavEntry("all", "Everything", "Every title in your library", "all", Nav));
        return Xml(feed, Nav);
    }

    [HttpGet("opensearch.xml")]
    public IActionResult OpenSearch()
    {
        XNamespace os = "http://a9.com/-/spec/opensearch/1.1/";
        var doc = new XElement(os + "OpenSearchDescription",
            new XElement(os + "ShortName", config.InstanceName),
            new XElement(os + "Description", "Search your Issuarr library"),
            new XElement(os + "InputEncoding", "UTF-8"),
            new XElement(os + "Url", new XAttribute("type", Nav), new XAttribute("template", Link("search") + (Link("search").Contains('?') ? "&" : "?") + "q={searchTerms}")));
        return Content(new XDeclaration("1.0", "utf-8", null) + doc.ToString(), "application/opensearchdescription+xml", Encoding.UTF8);
    }

    [HttpGet("kind/{kind}")]
    public IActionResult Kind(TitleKind kind, [FromQuery] int page = 1) =>
        TitleList($"kind-{kind}", kind switch { TitleKind.Comic => "Comics", TitleKind.Magazine => "Magazines", _ => "eBooks" },
            titles.All().Where(t => t.Kind == kind), $"kind/{kind.ToString().ToLowerInvariant()}", page);

    [HttpGet("all")]
    public IActionResult All([FromQuery] int page = 1) => TitleList("all", "Everything", titles.All(), "all", page);

    [HttpGet("search")]
    public IActionResult Search([FromQuery] string? q, [FromQuery] int page = 1)
    {
        var term = (q ?? "").Trim();
        var matches = titles.All().Where(t => term.Length > 0 && (t.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || (t.Publisher ?? "").Contains(term, StringComparison.OrdinalIgnoreCase)));
        return TitleList("search", $"Search: {term}", matches, $"search?q={Uri.EscapeDataString(term)}", page);
    }

    [HttpGet("title/{titleId:int}")]
    public IActionResult Title(int titleId, [FromQuery] int page = 1)
    {
        var title = titles.Get(titleId);
        if (title == null)
        {
            return NotFound();
        }

        var fileMap = files.GetByTitle(titleId).ToDictionary(f => f.Id);
        var withFiles = issues.GetByTitle(titleId).Where(i => i.IssueFileId != null && fileMap.ContainsKey(i.IssueFileId.Value))
            .OrderBy(i => i.SortKey).ToList();
        var feed = Feed($"title-{titleId}", title.Name, Acq);
        if (TitleCoverLink(title) is { } cover)
        {
            feed.Add(new XElement(Atom + "icon", cover));
        }

        foreach (var i in Paged(withFiles, page, $"title/{titleId}", feed))
        {
            feed.Add(IssueEntry(title, i, fileMap[i.IssueFileId!.Value]));
        }

        return Xml(feed, Acq);
    }

    [HttpGet("reading")]
    public IActionResult Reading()
    {
        var shelves = home.Get(50);
        return ShelfFeed("reading", "Continue Reading", shelves.ContinueReading.Concat(shelves.UpNext));
    }

    [HttpGet("recent")]
    public IActionResult Recent() => ShelfFeed("recent", "Recently Added", home.Get(50).RecentlyAdded);

    [HttpGet("file/{issueId:int}")]
    public IActionResult File(int issueId)
    {
        if (Resolve(issueId) is not { } r)
        {
            return NotFound();
        }

        return PhysicalFile(r.Path, Mime(r.File.Format), Path.GetFileName(r.Path), enableRangeProcessing: true);
    }

    [HttpGet("page/{issueId:int}/{page:int}")]
    public IActionResult Page(int issueId, int page)
    {
        if (Resolve(issueId) is not { } r)
        {
            return NotFound();
        }

        return ComicArchive.Page(r.Path, page) is { } p ? File(p.Data, p.ContentType) : NotFound();
    }

    [HttpGet("cover/title/{titleId:int}")]
    public IActionResult TitleCover(int titleId)
    {
        var title = titles.Get(titleId);
        if (title == null)
        {
            return NotFound();
        }

        if (covers.CustomCoverPath(title) is { } path && System.IO.File.Exists(path))
        {
            return PhysicalFile(path, Path.GetExtension(path) == ".png" ? "image/png" : Path.GetExtension(path) == ".webp" ? "image/webp" : "image/jpeg");
        }

        // Fall back to the first file's own cover, then the metadata image.
        var first = issues.GetByTitle(titleId).Where(i => i.IssueFileId != null).OrderBy(i => i.SortKey).FirstOrDefault();
        if (first != null && covers.IssueCover(first.Id) is { } c)
        {
            return File(c.Data, c.ContentType);
        }

        return title.CoverUrl != null ? Redirect(title.CoverUrl) : NotFound();
    }

    [HttpGet("cover/issue/{issueId:int}")]
    public IActionResult IssueCover(int issueId)
    {
        if (covers.IssueCover(issueId) is { } c)
        {
            return File(c.Data, c.ContentType);
        }

        var issue = issues.Get(issueId);
        if (issue?.CoverUrl != null)
        {
            return Redirect(issue.CoverUrl);
        }

        return issue != null ? TitleCover(issue.TitleId) : NotFound();
    }

    // ------------------------------------------------------------------

    private IActionResult TitleList(string id, string name, IEnumerable<Title> list, string path, int page)
    {
        var all = list.OrderBy(t => t.SortName).ToList();
        var counts = files.All().GroupBy(f => f.TitleId).ToDictionary(g => g.Key, g => g.Count());
        var feed = Feed(id, name, Nav);
        foreach (var t in Paged(all, page, path, feed))
        {
            var count = counts.GetValueOrDefault(t.Id);
            var entry = Entry($"title:{t.Id}", t.Name,
                $"{count} {(t.Kind == TitleKind.Book ? (count == 1 ? "book" : "books") : count == 1 ? "issue" : "issues")}" + (t.Publisher != null ? $" · {t.Publisher}" : ""));
            entry.Add(LinkEl("subsection", Link($"title/{t.Id}"), Acq));
            AddImages(entry, Link($"cover/title/{t.Id}"));
            feed.Add(entry);
        }

        return Xml(feed, Nav);
    }

    private IActionResult ShelfFeed(string id, string name, IEnumerable<ShelfItem> items)
    {
        var feed = Feed(id, name, Acq);
        foreach (var s in items.DistinctBy(s => s.IssueId))
        {
            if (titles.Get(s.TitleId) is { } title && issues.Get(s.IssueId) is { IssueFileId: { } fid } issue && files.Get(fid) is { } file)
            {
                var entry = IssueEntry(title, issue, file);
                if (s.Percent is { } p && p > 0)
                {
                    entry.Element(Atom + "content")!.Value = $"{Math.Round(p)}% read. " + entry.Element(Atom + "content")!.Value;
                }

                feed.Add(entry);
            }
        }

        return Xml(feed, Acq);
    }

    private XElement IssueEntry(Title title, Issue issue, IssueFile file)
    {
        var name = title.Kind == TitleKind.Book ? issue.Number : $"{title.Name} {issue.Number}";
        var summary = string.Join(" · ", new[] { issue.IssueTitle, issue.ReleaseDate[..4], file.Format, $"{file.Size / 1024.0 / 1024:0.#} MB" }.Where(x => !string.IsNullOrEmpty(x)));
        var entry = Entry($"issue:{issue.Id}", name, summary);
        entry.Add(new XElement(Atom + "author", new XElement(Atom + "name", title.Kind == TitleKind.Book ? title.Name : title.Publisher ?? title.Name)));
        entry.Add(new XElement(Dc + "issued", issue.ReleaseDate));
        entry.Add(LinkEl("http://opds-spec.org/acquisition", Link($"file/{issue.Id}"), Mime(file.Format), Path.GetFileName(file.RelativePath)));
        AddImages(entry, Link($"cover/issue/{issue.Id}"));

        if (file.Format.ToUpperInvariant() is "CBZ" or "CBR" or "CB7")
        {
            // OPDS-PSE: stream pages instead of downloading the whole archive.
            int count;
            try
            {
                count = ComicArchive.Pages(Path.Combine(title.Path, file.RelativePath)).Count;
            }
            catch (Exception)
            {
                count = 0;
            }

            if (count > 0)
            {
                var pse = LinkEl("http://vaemendis.net/opds-pse/stream", Link($"page/{issue.Id}/") + "{pageNumber}" + ApiKeySuffix(), "image/jpeg");
                pse.Add(new XAttribute(Pse + "count", count));
                entry.Add(pse);
            }
        }

        return entry;
    }

    private IEnumerable<T> Paged<T>(List<T> all, int page, string path, XElement feed)
    {
        page = Math.Max(1, page);
        var pages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)PageSize));
        var sep = path.Contains('?') ? "&" : "?";
        if (page < pages)
        {
            feed.Add(LinkEl("next", Link($"{path}{sep}page={page + 1}"), feed.Attribute("kind")?.Value ?? Nav));
        }

        if (page > 1)
        {
            feed.Add(LinkEl("previous", Link($"{path}{sep}page={page - 1}"), Nav));
        }

        return all.Skip((page - 1) * PageSize).Take(PageSize);
    }

    private XElement Feed(string id, string title, string kind)
    {
        var feed = new XElement(Atom + "feed",
            new XAttribute(XNamespace.Xmlns + "opds", Opds),
            new XAttribute(XNamespace.Xmlns + "pse", Pse),
            new XAttribute(XNamespace.Xmlns + "dcterms", Dc),
            new XElement(Atom + "id", $"urn:issuarr:{id}"),
            new XElement(Atom + "title", title),
            new XElement(Atom + "updated", DateTime.UtcNow.ToString("O")),
            new XElement(Atom + "author", new XElement(Atom + "name", "Issuarr")),
            LinkEl("self", Link(Request.Path.Value!.TrimStart('/').Substring(4).TrimStart('/') + Request.QueryString.Value?.Replace($"apikey={Request.Query["apikey"]}", "").TrimEnd('&', '?')), kind),
            LinkEl("start", Link(""), Nav),
            LinkEl("search", Link("opensearch.xml"), "application/opensearchdescription+xml"));
        return feed;
    }

    private static XElement Entry(string id, string title, string content) =>
        new(Atom + "entry",
            new XElement(Atom + "title", title),
            new XElement(Atom + "id", $"urn:issuarr:{id}"),
            new XElement(Atom + "updated", DateTime.UtcNow.ToString("O")),
            new XElement(Atom + "content", new XAttribute("type", "text"), content));

    private XElement NavEntry(string id, string title, string content, string path, string type)
    {
        var e = Entry(id, title, content);
        e.Add(LinkEl("subsection", Link(path), type));
        return e;
    }

    private static void AddImages(XElement entry, string url)
    {
        entry.Add(LinkEl("http://opds-spec.org/image", url, "image/jpeg"));
        entry.Add(LinkEl("http://opds-spec.org/image/thumbnail", url, "image/jpeg"));
    }

    private static XElement LinkEl(string rel, string href, string type, string? title = null)
    {
        var l = new XElement(Atom + "link", new XAttribute("rel", rel), new XAttribute("href", href), new XAttribute("type", type));
        if (title != null)
        {
            l.Add(new XAttribute("title", title));
        }

        return l;
    }

    /// <summary>Links keep ?apikey= when the catalog was opened with one (for apps without Basic auth).</summary>
    private string Link(string path)
    {
        var url = $"{config.UrlBase}/opds{(path.Length > 0 ? "/" + path.TrimStart('/') : "")}";
        var key = Request.Query["apikey"].FirstOrDefault();
        return key == null ? url : url + (url.Contains('?') ? "&" : "?") + "apikey=" + Uri.EscapeDataString(key);
    }

    private string ApiKeySuffix() => Request.Query["apikey"].FirstOrDefault() is { } key ? "?apikey=" + Uri.EscapeDataString(key) : "";

    private string? TitleCoverLink(Title t) => Link($"cover/title/{t.Id}");

    private IActionResult Xml(XElement feed, string type) =>
        Content(new XDeclaration("1.0", "utf-8", null) + Environment.NewLine + feed, type, Encoding.UTF8);

    private (IssueFile File, string Path)? Resolve(int issueId)
    {
        var issue = issues.Get(issueId);
        if (issue?.IssueFileId is not { } fid || files.Get(fid) is not { } file || titles.Get(issue.TitleId) is not { } title)
        {
            return null;
        }

        var path = System.IO.Path.Combine(title.Path, file.RelativePath);
        return System.IO.File.Exists(path) ? (file, path) : null;
    }

    private static string Mime(string format) => format.ToUpperInvariant() switch
    {
        "PDF" => "application/pdf",
        "EPUB" => "application/epub+zip",
        "CBZ" => "application/vnd.comicbook+zip",
        "CBR" => "application/vnd.comicbook-rar",
        "CB7" => "application/x-cb7",
        "MOBI" => "application/x-mobipocket-ebook",
        "AZW3" or "AZW" => "application/vnd.amazon.ebook",
        _ => "application/octet-stream",
    };
}
