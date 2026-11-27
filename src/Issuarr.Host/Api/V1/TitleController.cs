using Issuarr.Core.Issues;
using Issuarr.Core.Jobs;
using Issuarr.Core.Metadata;
using Issuarr.Core.Configuration;
using Issuarr.Core.Titles;
using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.Api.V1;

[ApiController]
[Route("api/v1/title")]
public class TitleController(
    ITitleService titles,
    ITitleManagementService management,
    IEnumerable<IMetadataProvider> metadata,
    IConfigService config,
    ICommandService commands,
    IIssueFileRepository files,
    ResourceMapper mapper) : ControllerBase
{
    [HttpGet]
    public List<TitleResource> GetAll([FromQuery] bool includeIssues = false) => mapper.Titles(titles.All(), includeIssues);

    [HttpGet("{id:int}")]
    public ActionResult<TitleResource> Get(int id)
    {
        var title = titles.Get(id);
        return title == null ? NotFound() : mapper.Title(title, includeIssues: true);
    }

    [HttpGet("slug/{slug}")]
    public ActionResult<TitleResource> GetBySlug(string slug)
    {
        var title = titles.FindBySlug(slug);
        return title == null ? NotFound() : mapper.Title(title, includeIssues: true);
    }

    /// <summary>Search the metadata source (ComicVine or Metron) for series to add.</summary>
    [HttpGet("lookup")]
    public async Task<ActionResult<object>> Lookup([FromQuery] string term, CancellationToken ct)
    {
        var provider = metadata.FirstOrDefault(m => m.Source == config.MetadataSource) ?? metadata.First();
        try
        {
            var results = await provider.Search(term, ct);
            var existing = titles.All().Where(t => t.MetadataSource == provider.Source && t.ExternalId != null)
                .ToDictionary(t => t.ExternalId!, t => t);
            return results.Select(r => new
            {
                r.Source, r.ExternalId, r.Name, r.Year, r.Publisher, r.IssueCount, r.Overview, r.CoverUrl, r.SiteUrl,
                existingTitleId = existing.GetValueOrDefault(r.ExternalId)?.Id,
                existingSlug = existing.GetValueOrDefault(r.ExternalId)?.Slug,
            }).ToList();
        }
        catch (MetadataException e)
        {
            return BadRequest(new { message = e.Message });
        }
        catch (HttpRequestException e)
        {
            return BadRequest(new { message = $"Couldn't reach {provider.DisplayName}: {e.Message}" });
        }
    }

    [HttpPost]
    public async Task<ActionResult<TitleResource>> Add([FromBody] AddTitleResource resource, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            return ValidationProblem("Name is required");
        }

        if (string.IsNullOrWhiteSpace(resource.RootFolderPath) && string.IsNullOrWhiteSpace(resource.Path))
        {
            return ValidationProblem("Choose a root folder");
        }

        var model = resource.ToModel();
        model.MetadataSource = resource.MetadataSource;
        model.ExternalId = resource.ExternalId;
        model.CoverUrl = resource.CoverUrl;
        if (model.Palette.Count == 0)
        {
            model.Palette = DefaultPalette(model.Name);
        }

        var options = new AddTitleOptions
        {
            Monitor = Enum.TryParse<MonitorOption>(resource.Monitor, true, out var m) ? m : MonitorOption.All,
            SearchForMissing = resource.SearchForMissing,
            FirstIssueDate = DateOnly.TryParse(resource.FirstIssueDate, out var d) ? d : null,
        };

        Title added;
        string? warning = null;
        try
        {
            added = await management.Add(model, resource.RootFolderPath ?? "", options, ct);
        }
        catch (TitleAddedWithWarningException e)
        {
            added = e.Title;
            warning = e.Message;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return ValidationProblem(e.Message);
        }

        if (options.SearchForMissing && options.Monitor != MonitorOption.None)
        {
            commands.Enqueue(CommandNames.TitleSearch, added.Id);
        }

        Response.Headers["X-Issuarr-Warning"] = warning ?? "";
        return CreatedAtAction(nameof(Get), new { id = added.Id }, mapper.Title(added, includeIssues: true));
    }

    [HttpGet("{id:int}/files")]
    public ActionResult<object> Files(int id)
    {
        var title = titles.Get(id);
        if (title == null)
        {
            return NotFound();
        }

        return files.GetByTitle(id).Select(f => new { f.Id, f.RelativePath, f.Size, f.Format, f.DateAdded, exists = System.IO.File.Exists(Path.Combine(title.Path, f.RelativePath)) }).ToList();
    }

    [HttpGet("{id:int}/rename")]
    public ActionResult<List<RenamePreview>> RenamePreview(int id) => titles.Get(id) == null ? NotFound() : management.PreviewRename(id);

    private static List<string> DefaultPalette(string name)
    {
        string[][] palettes =
        [
            ["#3a1c71", "#d76d77"], ["#0f2027", "#2c5364"], ["#134e5e", "#71b280"], ["#fc466b", "#3f5efb"], ["#f12711", "#f5af19"],
            ["#8e2de2", "#4a00e0"], ["#2c3e50", "#4ca1af"], ["#56ab2f", "#a8e063"], ["#cb2d3e", "#ef473a"], ["#0b486b", "#f56217"],
        ];
        var hash = name.Aggregate(17, (h, c) => unchecked(h * 31 + c));
        return [.. palettes[(int)((uint)hash % palettes.Length)]];
    }

    [HttpPut("{id:int}")]
    public ActionResult<TitleResource> Update(int id, [FromBody] TitleResource resource)
    {
        if (titles.Get(id) == null)
        {
            return NotFound();
        }

        var model = resource.ToModel();
        model.Id = id;
        var updated = titles.Update(model);
        return mapper.Title(updated, includeIssues: false);
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id, [FromQuery] bool deleteFiles = false)
    {
        if (titles.Get(id) == null)
        {
            return NotFound();
        }

        try
        {
            management.Delete(id, deleteFiles);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return BadRequest(new { message = $"Title removed from the database check, but deleting files failed: {e.Message}" });
        }

        return Ok();
    }
}

[ApiController]
[Route("api/v1/issuefile")]
public class IssueFileController(ITitleManagementService management) : ControllerBase
{
    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        try
        {
            management.DeleteIssueFile(id);
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
}
