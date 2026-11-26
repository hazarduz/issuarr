using Issuarr.Core.Issues;
using Issuarr.Core.Titles;
using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.Api.V1;

[ApiController]
[Route("api/v1/issue")]
public class IssueController(IIssueRepository issues, IIssueFileRepository files, ITitleService titles, ResourceMapper mapper)
    : ControllerBase
{
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
public class WantedController(IIssueRepository issues, ITitleService titles, ResourceMapper mapper) : ControllerBase
{
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
