using Issuarr.Core.LibraryImport;
using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.Api.V1;

[ApiController]
[Route("api/v1/libraryimport")]
public class LibraryImportController(ILibraryImportService import, Issuarr.Core.Titles.IRefreshTitleService refresh) : ControllerBase
{
    /// <summary>Scan a folder and list the titles that could be imported from it.</summary>
    [HttpGet]
    public ActionResult<List<ImportCandidate>> Scan([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            return ValidationProblem("Choose an absolute folder path");
        }

        try
        {
            return import.Scan(path);
        }
        catch (DirectoryNotFoundException e)
        {
            return NotFound(new { message = e.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = $"Permission denied: {path}" });
        }
    }

    [HttpPost]
    public async Task<ActionResult<List<ImportResult>>> Import([FromBody] List<ImportRequest> items, CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return ValidationProblem("Nothing selected to import");
        }

        var results = import.Import(items);

        // Titles matched to ComicVine/Metron get their full issue list straight away.
        foreach (var (req, result) in items.Zip(results))
        {
            if (result.TitleId is { } id && !string.IsNullOrEmpty(req.ExternalId))
            {
                try
                {
                    await refresh.Refresh(id, ct);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    result.Warning = $"Imported, but couldn't load issues from the metadata source: {e.Message}";
                }
            }
        }

        return results;
    }
}
