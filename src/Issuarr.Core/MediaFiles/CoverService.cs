using System.IO.Compression;
using System.Xml.Linq;
using Issuarr.Core.Configuration;
using Issuarr.Core.Issues;
using Issuarr.Core.Providers;
using Issuarr.Core.Reading;
using Issuarr.Core.Titles;

namespace Issuarr.Core.MediaFiles;

public record CoverCandidate(string Source, string Url, string Label);

public interface ICoverService
{
    /// <summary>Images that could be used as a title's cover (metadata, issue covers, first pages of files).</summary>
    List<CoverCandidate> Candidates(int titleId);

    Task SetFromBytes(int titleId, byte[] data, CancellationToken ct);
    Task SetFromUrl(int titleId, string url, CancellationToken ct);
    void Reset(int titleId);
    string? CustomCoverPath(Title title);

    /// <summary>A cover for one issue/book: the file's first page (comics) or embedded cover (EPUB).</summary>
    (byte[] Data, string ContentType)? IssueCover(int issueId);
}

public class CoverService(AppFolders folders, ITitleRepository titles, IIssueRepository issues, IIssueFileRepository files, IHttpClientFactory http) : ICoverService
{
    private const int MaxBytes = 15 * 1024 * 1024;
    private static readonly HashSet<string> Readable = ["CBZ", "CBR", "CB7", "EPUB"];

    private string Folder => Path.Combine(folders.AppDataFolder, "MediaCover");

    public List<CoverCandidate> Candidates(int titleId)
    {
        var title = titles.Get(titleId) ?? throw new KeyNotFoundException("Title not found");
        var list = new List<CoverCandidate>();
        if (!string.IsNullOrEmpty(title.CoverUrl))
        {
            list.Add(new("metadata", title.CoverUrl, title.MetadataSource switch
            {
                "comicvine" => "ComicVine",
                "metron" => "Metron",
                "openlibrary" => "Open Library",
                "googlebooks" => "Google Books",
                "wikidata" => "Wikidata",
                _ => "Current",
            }));
        }

        var all = issues.GetByTitle(titleId).OrderBy(i => i.SortKey).ToList();
        var fileMap = files.GetByTitle(titleId).ToDictionary(f => f.Id);

        // Covers the metadata source has for individual issues/books (first issue first).
        foreach (var i in all.Where(i => !string.IsNullOrEmpty(i.CoverUrl)).Take(40))
        {
            if (list.All(c => c.Url != i.CoverUrl))
            {
                list.Add(new("issue", i.CoverUrl!, i.Number));
            }
        }

        // First pages / embedded covers of files on disk (PDF page 1 is rendered on the server and cached).
        foreach (var i in all.Where(i => i.IssueFileId != null).Take(40))
        {
            if (!fileMap.TryGetValue(i.IssueFileId!.Value, out var f))
            {
                continue;
            }

            var format = f.Format.ToUpperInvariant();
            if (Readable.Contains(format))
            {
                list.Add(new("file", $"/api/v1/read/{i.Id}/cover", $"{i.Number} (file)"));
            }
            else if (format == "PDF")
            {
                list.Add(new("file", $"/api/v1/read/{i.Id}/cover", $"{i.Number} (page 1)"));
            }
        }

        return list;
    }

    public async Task SetFromBytes(int titleId, byte[] data, CancellationToken ct)
    {
        var title = titles.Get(titleId) ?? throw new KeyNotFoundException("Title not found");
        if (data.Length == 0 || data.Length > MaxBytes)
        {
            throw new ArgumentException("Images must be under 15 MB");
        }

        var ext = ImageExtension(data) ?? throw new ArgumentException("That isn't a JPEG, PNG, WebP or GIF image");
        Directory.CreateDirectory(Path.Combine(Folder, titleId.ToString()));

        // Remove the previous custom cover, then save under a new name so browsers fetch the new one.
        if (CustomCoverPath(title) is { } old && File.Exists(old))
        {
            File.Delete(old);
        }

        var name = $"cover-{DateTime.UtcNow.Ticks}{ext}";
        await File.WriteAllBytesAsync(Path.Combine(Folder, titleId.ToString(), name), data, ct);
        title.CustomCover = name;
        titles.Update(title);
    }

    public async Task SetFromUrl(int titleId, string url, CancellationToken ct)
    {
        // Covers taken from our own files don't need a round trip over HTTP.
        var local = System.Text.RegularExpressions.Regex.Match(url, @"^/api/v1/read/(\d+)/cover");
        if (local.Success)
        {
            var cover = IssueCover(int.Parse(local.Groups[1].Value)) ?? throw new ArgumentException("That file has no cover image");
            await SetFromBytes(titleId, cover.Data, ct);
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            throw new ArgumentException("Enter a full http(s):// image address");
        }

        using var res = await http.CreateClient(Http.Client).GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode)
        {
            throw new ArgumentException($"Couldn't download the image (HTTP {(int)res.StatusCode})");
        }

        if (res.Content.Headers.ContentLength > MaxBytes)
        {
            throw new ArgumentException("Images must be under 15 MB");
        }

        await SetFromBytes(titleId, await res.Content.ReadAsByteArrayAsync(ct), ct);
    }

    public void Reset(int titleId)
    {
        var title = titles.Get(titleId) ?? throw new KeyNotFoundException("Title not found");
        if (CustomCoverPath(title) is { } path && File.Exists(path))
        {
            File.Delete(path);
        }

        title.CustomCover = null;
        titles.Update(title);
    }

    public string? CustomCoverPath(Title title) =>
        title.CustomCover == null ? null : Path.Combine(Folder, title.Id.ToString(), Path.GetFileName(title.CustomCover));

    public (byte[] Data, string ContentType)? IssueCover(int issueId)
    {
        var issue = issues.Get(issueId);
        if (issue?.IssueFileId is not { } fileId || files.Get(fileId) is not { } file || titles.Get(issue.TitleId) is not { } title)
        {
            return null;
        }

        var path = Path.Combine(title.Path, file.RelativePath);
        if (!File.Exists(path))
        {
            return null;
        }

        // Covers are cached as small JPEGs: PDFs are slow to render and comic pages can be multi-MB scans.
        var dir = Path.Combine(Folder, "issues");
        var cached = Path.Combine(dir, $"{file.Id}-{file.Size}.jpg");
        if (File.Exists(cached))
        {
            return (File.ReadAllBytes(cached), "image/jpeg");
        }

        var format = file.Format.ToUpperInvariant();
        (byte[] Data, string ContentType)? raw;
        try
        {
            raw = format switch
            {
                "CBZ" or "CBR" or "CB7" => ComicArchive.Page(path, 0),
                "EPUB" => EpubCover(path),
                "PDF" => RenderPdf(path),
                _ => null,
            };
        }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or System.Xml.XmlException or SharpCompress.Common.ArchiveException)
        {
            return null;
        }

        if (raw == null)
        {
            return null;
        }

        var thumb = format == "PDF" ? raw.Value.Data : Shrink(raw.Value.Data);
        if (thumb == null)
        {
            return raw; // Not an image we can decode (e.g. SVG); serve it as-is, uncached.
        }

        try
        {
            Directory.CreateDirectory(dir);
            foreach (var stale in Directory.EnumerateFiles(dir, $"{file.Id}-*.jpg"))
            {
                File.Delete(stale);
            }

            File.WriteAllBytes(cached, thumb);
        }
        catch (IOException)
        {
            // Another request is writing the same thumbnail; ours is still good to return.
        }

        return (thumb, "image/jpeg");
    }

    private const int ThumbWidth = 600;
    private static readonly SemaphoreSlim PdfLock = new(1, 1);

    private static (byte[] Data, string ContentType)? RenderPdf(string path)
    {
        // PDFium is single-threaded; render one at a time so a grid of 200 magazines doesn't swamp the CPU.
        PdfLock.Wait();
        try
        {
            return RenderPdfPage(path) is { } jpg ? (jpg, "image/jpeg") : null;
        }
        finally
        {
            PdfLock.Release();
        }
    }

    /// <summary>Renders the first page of a PDF as a JPEG, or null if it can't be read.</summary>
    public static byte[]? RenderPdfPage(string path, int width = ThumbWidth)
    {
        try
        {
            using var pdf = File.OpenRead(path);
            using var image = new MemoryStream();
#pragma warning disable CA1416 // Linux, Windows and macOS are all supported by PDFtoImage.
            PDFtoImage.Conversion.SaveJpeg(image, pdf, 0, leaveOpen: false, password: null,
                options: new PDFtoImage.RenderOptions(Width: width, WithAspectRatio: true, WithAnnotations: true));
#pragma warning restore CA1416
            return image.ToArray();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Damaged/encrypted PDFs, or the PDF engine isn't available on this platform.
            return null;
        }
    }

    /// <summary>Scales an image down to at most <see cref="ThumbWidth"/> wide as a JPEG; null if it can't be decoded.</summary>
    public static byte[]? Shrink(byte[] data, int width = ThumbWidth)
    {
        try
        {
            using var bitmap = SkiaSharp.SKBitmap.Decode(data);
            if (bitmap == null)
            {
                return null;
            }

            using var scaled = bitmap.Width > width
                ? bitmap.Resize(new SkiaSharp.SKImageInfo(width, (int)Math.Round(bitmap.Height * (double)width / bitmap.Width)), new SkiaSharp.SKSamplingOptions(SkiaSharp.SKCubicResampler.Mitchell))
                : null;
            using var image = SkiaSharp.SKImage.FromBitmap(scaled ?? bitmap);
            using var jpg = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 85);
            return jpg?.ToArray();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Finds an EPUB's cover image (EPUB 3 "cover-image" property or EPUB 2 &lt;meta name="cover"&gt;).</summary>
    public static (byte[] Data, string ContentType)? EpubCover(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var container = zip.GetEntry("META-INF/container.xml");
        if (container == null)
        {
            return null;
        }

        XNamespace cns = "urn:oasis:names:tc:opendocument:xmlns:container";
        string? opfPath;
        using (var s = container.Open())
        {
            opfPath = XDocument.Load(s).Descendants(cns + "rootfile").FirstOrDefault()?.Attribute("full-path")?.Value;
        }

        var opfEntry = opfPath == null ? null : zip.GetEntry(opfPath);
        if (opfEntry == null)
        {
            return null;
        }

        XDocument opf;
        using (var s = opfEntry.Open())
        {
            opf = XDocument.Load(s);
        }

        var items = opf.Descendants().Where(e => e.Name.LocalName == "item").ToList();
        var coverId = opf.Descendants().FirstOrDefault(e => e.Name.LocalName == "meta" && e.Attribute("name")?.Value == "cover")?.Attribute("content")?.Value;
        var item = items.FirstOrDefault(i => (i.Attribute("properties")?.Value ?? "").Split(' ').Contains("cover-image"))
                   ?? items.FirstOrDefault(i => coverId != null && i.Attribute("id")?.Value == coverId)
                   ?? items.FirstOrDefault(i => (i.Attribute("media-type")?.Value ?? "").StartsWith("image/") && (i.Attribute("href")?.Value ?? "").Contains("cover", StringComparison.OrdinalIgnoreCase));
        var href = item?.Attribute("href")?.Value;
        if (href == null)
        {
            return null;
        }

        var baseDir = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? "";
        var full = string.IsNullOrEmpty(baseDir) ? href : $"{baseDir}/{href}";
        full = Uri.UnescapeDataString(System.Text.RegularExpressions.Regex.Replace(full, @"[^/]+/\.\./", ""));
        var entry = zip.GetEntry(full);
        if (entry == null)
        {
            return null;
        }

        using var es = entry.Open();
        using var ms = new MemoryStream();
        es.CopyTo(ms);
        return (ms.ToArray(), item!.Attribute("media-type")?.Value ?? ComicArchive.ContentType(full));
    }

    public static string? ImageExtension(byte[] d) =>
        d.Length < 12 ? null
        : d[0] == 0xFF && d[1] == 0xD8 ? ".jpg"
        : d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47 ? ".png"
        : d[0] == 'G' && d[1] == 'I' && d[2] == 'F' ? ".gif"
        : d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P' ? ".webp"
        : null;
}
