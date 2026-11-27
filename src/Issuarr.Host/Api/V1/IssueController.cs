using Issuarr.Core.Issues;
using Issuarr.Core.Titles;
using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.Api.V1;

public record IssueEditRequest(string Number, string? Title, string ReleaseDate);

public record IssueMatchRequest(string ExternalId);

public record ReleaseDateRequest(string? ReleaseDate);

[ApiController]
[Route("api/v1/issue")]
public class IssueController(IIssueRepository issues, IIssueFileRepository files, ITitleService titles, IIssueManagementService manage, ResourceMapper mapper)
    : ControllerBase
{
    [HttpPut("{id:int}")]
    public ActionResult<IssueResource> Edit(int id, [FromBody] IssueEditRequest req) => Run(() => manage.Edit(id, req.Number, req.Title, req.ReleaseDate));

    /// <summary>Set the release date by hand (null unlocks it so metadata refreshes update it again).</summary>
    [HttpPut("{id:int}/releasedate")]
    public ActionResult<IssueResource> SetReleaseDate(int id, [FromBody] ReleaseDateRequest req) => Run(() => manage.SetReleaseDate(id, req.ReleaseDate));

    [HttpPost("{id:int}/match")]
    public async Task<ActionResult<IssueResource>> Match(int id, [FromBody] IssueMatchRequest req, CancellationToken ct)
    {
        try
        {
            return ToResource(await manage.Match(id, req.ExternalId, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception e) when (e is ArgumentException or Core.Metadata.MetadataException or HttpRequestException)
        {
            return BadRequest(new { message = e.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id, [FromQuery] bool deleteFile = false)
    {
        try
        {
            manage.Delete(id, deleteFile);
            return Ok();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return BadRequest(new { message = e.Message });
        }
    }

    private ActionResult<IssueResource> Run(Func<Issue> action)
    {
        try
        {
            return ToResource(action());
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException e)
        {
            return BadRequest(new { message = e.Message });
        }
    }

    private IssueResource ToResource(Issue issue)
    {
        var title = titles.Get(issue.TitleId)!;
        return mapper.Issue(issue, title, files.GetByTitle(title.Id).ToDictionary(f => f.Id));
    }

    [HttpGet]
    public ActionResult<List<IssueResource>> GetByTitle([FromQuery] int titleId)
    {
        var title = titles.Get(titleId);
        if (title == null)
        {
            return NotFound();
        }

        var fileMap = files.GetByTitle(titleId).ToDictionary(f => f.Id);
        return issues.GetByTitle(titleId).Select(i => mapper.Issue(i, title, fileMap)).ToList();
    }

    [HttpPut("monitor")]
    public IActionResult SetMonitored([FromBody] MonitorIssuesResource resource)
    {
        if (resource.IssueIds.Count == 0)
        {
            return ValidationProblem("issueIds must not be empty");
        }

        issues.SetMonitored(resource.IssueIds, resource.Monitored);
        return Accepted();
    }
}

[ApiController]
[Route("api/v1/calendar")]
public class CalendarController(IIssueRepository issues, IIssueFileRepository files, ITitleService titles, ResourceMapper mapper)
    : ControllerBase
{
    [HttpGet]
    public List<IssueResource> Get([FromQuery] DateOnly? start, [FromQuery] DateOnly? end)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var from = (start ?? today.AddDays(-7)).ToString("yyyy-MM-dd");
        var to = (end ?? today.AddDays(28)).ToString("yyyy-MM-dd");
        var titleMap = titles.All().ToDictionary(t => t.Id);
        var fileMap = files.All().ToDictionary(f => f.Id);
        return issues.Between(from, to).Select(i => mapper.Issue(i, titleMap[i.TitleId], fileMap)).ToList();
    }
}

[ApiController]
[Route("api/v1/wanted")]
public class WantedController(IIssueRepository issues, IIssueFileRepository files, ITitleService titles, ResourceMapper mapper) : ControllerBase
{
    /// <summary>Issues whose file is in a worse format than the title's profile cutoff.</summary>
    [HttpGet("cutoff")]
    public List<IssueResource> CutoffUnmet()
    {
        var titleMap = titles.All().ToDictionary(t => t.Id);
        var fileMap = files.All().ToDictionary(f => f.Id);
        return issues.WithFiles()
            .Where(i => titleMap.TryGetValue(i.TitleId, out var t) && t.Monitored && i.Monitored && fileMap.TryGetValue(i.IssueFileId!.Value, out var f)
                        && Issuarr.Core.Decisions.QualityProfiles.Get(t.QualityProfile) is { UpgradesAllowed: true } p && p.Rank(f.Format) > 0)
            .OrderByDescending(i => i.ReleaseDate)
            .Select(i => mapper.Issue(i, titleMap[i.TitleId], fileMap))
            .ToList();
    }

    [HttpGet("missing")]
    public List<IssueResource> Missing()
    {
        var titleMap = titles.All().ToDictionary(t => t.Id);
        var noFiles = new Dictionary<int, IssueFile>();
        return issues.Missing(DateTime.Today.ToString("yyyy-MM-dd"))
            .Select(i => mapper.Issue(i, titleMap[i.TitleId], noFiles))
            .ToList();
    }
}
