using Issuarr.Core.Issues;
using Issuarr.Core.Reading;
using Issuarr.Core.Titles;
using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.Api.V1;

public record ProgressRequest(string Position, double Percent);

/// <summary>
/// Serves files to the built-in reader. Image and file URLs carry ?apikey= so the browser
/// can load them directly in &lt;img&gt; tags and readers.
/// </summary>
[ApiController]
[Route("api/v1/read")]
public class ReadController(IIssueRepository issues, IIssueFileRepository files, ITitleService titles, IReadProgressRepository progress) : ControllerBase
{
    private static readonly HashSet<string> Comic = ["CBZ", "CBR", "CB7"];

    [HttpGet("{issueId:int}")]
    public ActionResult<object> Info(int issueId)
    {
        if (Resolve(issueId) is not { } r)
        {
            return NotFound(new { message = "This issue has no file to read" });
        }

        var format = r.File.Format.ToUpperInvariant();
        var mode = Comic.Contains(format) ? "comic" : format == "PDF" ? "pdf" : format == "EPUB" ? "epub" : "download";
        int? pages = null;
        string? error = null;
        if (mode == "comic")
        {
            try
            {
                pages = ComicArchive.Pages(r.Path).Count;
                if (pages == 0)
                {
                    error = "No pages found in this archive";
                }
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or SharpCompress.Common.ArchiveException or InvalidDataException)
            {
                error = $"Couldn't open the archive: {e.Message}";
            }
        }

        // Neighbouring issues/books with files, for "next issue" navigation.
        var withFiles = issues.GetByTitle(r.Title.Id).Where(i => i.IssueFileId != null).OrderBy(i => i.SortKey).ToList();
        var index = withFiles.FindIndex(i => i.Id == issueId);
        var saved = progress.Get(issueId);

        return new
        {
            issueId,
            titleId = r.Title.Id,
            titleName = r.Title.Name,
            titleSlug = r.Title.Slug,
            kind = r.Title.Kind,
            number = r.Issue.Number,
            issueTitle = r.Issue.IssueTitle,
            format,
            mode,
            pages,
            size = r.File.Size,
            fileName = Path.GetFileName(r.File.RelativePath),
            error,
            progress = saved == null ? null : new { saved.Position, saved.Percent, saved.Updated },
            previousId = index > 0 ? withFiles[index - 1].Id : (int?)null,
            nextId = index >= 0 && index < withFiles.Count - 1 ? withFiles[index + 1].Id : (int?)null,
        };
    }

    [HttpGet("{issueId:int}/page/{page:int}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public IActionResult Page(int issueId, int page)
    {
        if (Resolve(issueId) is not { } r)
        {
            return NotFound();
        }

        try
        {
            return ComicArchive.Page(r.Path, page) is { } p ? File(p.Data, p.ContentType) : NotFound();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or SharpCompress.Common.ArchiveException or InvalidDataException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = e.Message });
        }
    }

    /// <summary>The whole file: for the PDF/EPUB readers (supports range requests) or as a download.</summary>
    [HttpGet("{issueId:int}/file")]
    public IActionResult Download(int issueId, [FromQuery] bool download = false)
    {
        if (Resolve(issueId) is not { } r)
        {
            return NotFound();
        }

        var contentType = r.File.Format.ToUpperInvariant() switch
        {
            "PDF" => "application/pdf",
            "EPUB" => "application/epub+zip",
            "CBZ" => "application/vnd.comicbook+zip",
            "CBR" => "application/vnd.comicbook-rar",
            "MOBI" => "application/x-mobipocket-ebook",
            "AZW3" => "application/vnd.amazon.ebook",
            _ => "application/octet-stream",
        };
        return PhysicalFile(r.Path, contentType, download ? Path.GetFileName(r.Path) : null, enableRangeProcessing: true);
    }

    [HttpPut("{issueId:int}/progress")]
    public IActionResult SaveProgress(int issueId, [FromBody] ProgressRequest req)
    {
        if (issues.Get(issueId) == null)
        {
            return NotFound();
        }

        progress.Save(new ReadProgress { IssueId = issueId, Position = req.Position, Percent = req.Percent });
        return Ok();
    }

    [HttpDelete("{issueId:int}/progress")]
    public IActionResult ClearProgress(int issueId)
    {
        progress.Clear(issueId);
        return Ok();
    }

    private (Issue Issue, Title Title, IssueFile File, string Path)? Resolve(int issueId)
    {
        var issue = issues.Get(issueId);
        if (issue?.IssueFileId is not { } fileId || files.Get(fileId) is not { } file || titles.Get(issue.TitleId) is not { } title)
        {
            return null;
        }

        var path = System.IO.Path.Combine(title.Path, file.RelativePath);
        return System.IO.File.Exists(path) ? (issue, title, file, path) : null;
    }
}
