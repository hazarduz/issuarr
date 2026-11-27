using Issuarr.Core.MediaFiles;
using Issuarr.Core.Reading;
using Issuarr.Core.Titles;
using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.Api.V1;

public record CoverUrlRequest(string Url);

[ApiController]
[Route("api/v1")]
public class CoverController(ICoverService covers, ITitleService titles, IReadingHomeService home) : ControllerBase
{
    /// <summary>A title's custom cover image.</summary>
    [HttpGet("mediacover/{titleId:int}")]
    [ResponseCache(Duration = 604800, Location = ResponseCacheLocation.Client)]
    public IActionResult Get(int titleId)
    {
        var title = titles.Get(titleId);
        var path = title == null ? null : covers.CustomCoverPath(title);
        if (path == null || !System.IO.File.Exists(path))
        {
            return NotFound();
        }

        var type = Path.GetExtension(path) switch { ".png" => "image/png", ".webp" => "image/webp", ".gif" => "image/gif", _ => "image/jpeg" };
        return PhysicalFile(path, type);
    }

    [HttpGet("title/{titleId:int}/covers")]
    public ActionResult<List<CoverCandidate>> Candidates(int titleId)
    {
        try
        {
            return covers.Candidates(titleId);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Upload an image (multipart "file").</summary>
    [HttpPost("title/{titleId:int}/cover")]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<IActionResult> Upload(int titleId, IFormFile file, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return await Run(() => covers.SetFromBytes(titleId, ms.ToArray(), ct));
    }

    /// <summary>Use an image from a URL or one of the candidates.</summary>
    [HttpPut("title/{titleId:int}/cover")]
    public Task<IActionResult> FromUrl(int titleId, [FromBody] CoverUrlRequest req, CancellationToken ct) =>
        Run(() => covers.SetFromUrl(titleId, req.Url, ct));

    [HttpDelete("title/{titleId:int}/cover")]
    public Task<IActionResult> Reset(int titleId) => Run(() =>
    {
        covers.Reset(titleId);
        return Task.CompletedTask;
    });

    /// <summary>An issue/book's cover taken from its file.</summary>
    [HttpGet("read/{issueId:int}/cover")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public IActionResult IssueCover(int issueId) =>
        covers.IssueCover(issueId) is { } c ? File(c.Data, c.ContentType) : NotFound();

    /// <summary>Home page shelves: continue reading, up next, recently added.</summary>
    [HttpGet("reading/home")]
    public ReadingHome Home() => home.Get();

    private async Task<IActionResult> Run(Func<Task> action)
    {
        try
        {
            await action();
            return Ok();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception e) when (e is ArgumentException or HttpRequestException or TaskCanceledException or IOException)
        {
            return BadRequest(new { message = e is TaskCanceledException ? "Timed out downloading the image" : e.Message });
        }
    }
}
