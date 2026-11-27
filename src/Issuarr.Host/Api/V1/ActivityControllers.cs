using Issuarr.Core.Blocklisting;
using Issuarr.Core.Decisions;
using Issuarr.Core.Download;
using Issuarr.Core.DownloadClients;
using Issuarr.Core.History;
using Issuarr.Core.IndexerSearch;
using Issuarr.Core.Issues;
using Issuarr.Core.Jobs;
using Issuarr.Core.Titles;
using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.Api.V1;

public record CommandRequest(string Name, int? TitleId, List<int>? IssueIds);

[ApiController]
[Route("api/v1/command")]
public class CommandController(ICommandService commands) : ControllerBase
{
    [HttpPost]
    public ActionResult<Command> Post([FromBody] CommandRequest req)
    {
        try
        {
            return commands.Enqueue(req.Name, req.TitleId, req.IssueIds);
        }
        catch (ArgumentException e)
        {
            return ValidationProblem(e.Message);
        }
    }

    [HttpGet]
    public IReadOnlyList<Command> Recent() => commands.Recent().Take(50).ToList();

    [HttpGet("{id:int}")]
    public ActionResult<Command> Get(int id) => commands.Get(id) is { } c ? c : NotFound();
}

public record ReleaseResource(
    string Guid, string Title, string Indexer, string Protocol, long Size, int? Seeders, int? Peers, string? PublishDate,
    string? InfoUrl, int? TitleId, string? TitleName, int? IssueId, string? IssueNumber, string? Format, bool Approved, List<string> Rejections);

public record GrabRequest(string Guid, int TitleId, int? IssueId);

[ApiController]
[Route("api/v1/release")]
public class ReleaseController(ISearchService search) : ControllerBase
{
    /// <summary>Interactive search: every release found for the title, with why it would or wouldn't be grabbed.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ReleaseResource>>> Get([FromQuery] int titleId, [FromQuery] int? issueId, CancellationToken ct)
    {
        try
        {
            var decisions = await search.Interactive(titleId, issueId, ct);
            return decisions.Select(d => new ReleaseResource(
                d.Release.Guid, d.Release.Title, d.Release.Indexer, d.Release.Protocol.ToString().ToLowerInvariant(), d.Release.Size,
                d.Release.Seeders, d.Release.Peers, d.Release.PublishDate?.ToString("O"), d.Release.InfoUrl,
                d.Title?.Id, d.Title?.Name, d.Issue?.Id, d.Issue?.Number, d.Format, d.Approved, d.Rejections)).ToList();
        }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(new { message = e.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Grab([FromBody] GrabRequest req, CancellationToken ct)
    {
        try
        {
            var id = await search.GrabRelease(req.Guid, req.TitleId, req.IssueId, ct);
            return Ok(new { downloadId = id });
        }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or Core.Providers.ProviderTestException or HttpRequestException)
        {
            return BadRequest(new { message = e.Message });
        }
    }
}

[ApiController]
[Route("api/v1/queue")]
public class QueueController(ITrackedDownloadService tracked, ICompletedDownloadService completed, ITitleService titles, IIssueRepository issues) : ControllerBase
{
    [HttpGet]
    public object Get()
    {
        var titleMap = titles.All().ToDictionary(t => t.Id);
        var records = tracked.All()
            .Where(t => t.State != TrackedState.Imported)
            .OrderBy(t => t.Item.Status == DownloadItemStatus.Completed)
            .Select(t =>
            {
                var issueNumbers = t.IssueIds.Count > 0 ? string.Join(", ", issues.Get(t.IssueIds).Select(i => i.Number)) : null;
                var title = t.TitleId is { } id ? titleMap.GetValueOrDefault(id) : null;
                return new
                {
                    id = t.Key,
                    titleId = title?.Id,
                    titleName = title?.Name,
                    titleSlug = title?.Slug,
                    issues = issueNumbers,
                    release = t.Item.Title,
                    downloadClient = t.Client.Name,
                    protocol = t.Protocol.ToString().ToLowerInvariant(),
                    indexer = t.Indexer,
                    size = t.Item.TotalSize,
                    sizeLeft = t.Item.RemainingSize,
                    progress = t.Item.TotalSize > 0 ? Math.Round(100.0 * (t.Item.TotalSize - t.Item.RemainingSize) / t.Item.TotalSize, 1) : t.Item.Status == DownloadItemStatus.Completed ? 100 : 0,
                    eta = t.Item.Eta?.TotalSeconds,
                    status = t.Item.Status.ToString().ToLowerInvariant(),
                    trackedState = t.State.ToString(),
                    messages = t.StatusMessages.Concat(t.Item.Message is { } m ? [m] : Array.Empty<string>()).ToList(),
                };
            }).ToList();
        return new { totalRecords = records.Count, records };
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Remove(string id, [FromQuery] bool removeFromClient = true, [FromQuery] bool blocklist = false, CancellationToken ct = default)
    {
        try
        {
            await completed.RemoveFromQueue(id, removeFromClient, blocklist, ct);
            return Ok();
        }
        catch (KeyNotFoundException e)
        {
            return NotFound(new { message = e.Message });
        }
        catch (Exception e) when (e is Core.Providers.ProviderTestException or HttpRequestException)
        {
            return BadRequest(new { message = e.Message });
        }
    }
}

[ApiController]
[Route("api/v1/history")]
public class HistoryController(IHistoryService history, ITitleService titles, IIssueRepository issues) : ControllerBase
{
    [HttpGet]
    public object Get([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] int? titleId = null)
    {
        var items = history.Page(page, Math.Clamp(pageSize, 1, 500), titleId, out var total);
        var titleMap = titles.All().ToDictionary(t => t.Id);
        var issueMap = issues.Get(items.Where(i => i.IssueId != null).Select(i => i.IssueId!.Value).Distinct()).ToDictionary(i => i.Id);
        return new
        {
            page,
            pageSize,
            totalRecords = total,
            records = items.Select(h => new
            {
                h.Id,
                h.TitleId,
                titleName = titleMap.GetValueOrDefault(h.TitleId)?.Name,
                titleSlug = titleMap.GetValueOrDefault(h.TitleId)?.Slug,
                h.IssueId,
                issue = h.IssueId is { } iid ? issueMap.GetValueOrDefault(iid)?.Number : null,
                eventType = h.EventType.ToString(),
                h.SourceTitle,
                h.Date,
                h.Data,
            }),
        };
    }

    [HttpDelete]
    public IActionResult Clear()
    {
        history.Clear();
        return Ok();
    }
}

[ApiController]
[Route("api/v1/blocklist")]
public class BlocklistController(IBlocklistService blocklist, ITitleService titles) : ControllerBase
{
    [HttpGet]
    public object Get()
    {
        var titleMap = titles.All().ToDictionary(t => t.Id);
        return blocklist.All().Select(b => new
        {
            b.Id, b.TitleId, titleName = titleMap.GetValueOrDefault(b.TitleId)?.Name, titleSlug = titleMap.GetValueOrDefault(b.TitleId)?.Slug,
            b.SourceTitle, protocol = b.Protocol.ToString().ToLowerInvariant(), b.Indexer, b.Message, b.Date,
        });
    }

    [HttpDelete("{id:int}")]
    public IActionResult Remove(int id)
    {
        blocklist.Remove(id);
        return Ok();
    }

    [HttpDelete]
    public IActionResult Clear()
    {
        blocklist.Clear();
        return Ok();
    }
}

[ApiController]
[Route("api/v1/qualityprofile")]
public class QualityProfileController : ControllerBase
{
    [HttpGet]
    public object Get() => QualityProfiles.All.Select(p => new { p.Name, p.Formats, p.Cutoff, p.UpgradesAllowed, p.Description });
}
