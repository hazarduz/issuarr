using Issuarr.Core.LibraryImport;
using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.Api.V1;

[ApiController]
[Route("api/v1/libraryimport")]
public class LibraryImportController(ILibraryImportService import) : ControllerBase
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
    public ActionResult<List<ImportResult>> Import([FromBody] List<ImportRequest> items)
    {
        if (items.Count == 0)
        {
            return ValidationProblem("Nothing selected to import");
        }

        return import.Import(items);
    }
}
