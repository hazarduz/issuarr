using Issuarr.Core.Titles;
using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.Api.V1;

[ApiController]
[Route("api/v1/title")]
public class TitleController(ITitleService titles, ResourceMapper mapper) : ControllerBase
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

    [HttpPost]
    public ActionResult<TitleResource> Add([FromBody] AddTitleResource resource)
    {
        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            return ValidationProblem("Name is required");
        }

        if (string.IsNullOrWhiteSpace(resource.RootFolderPath) && string.IsNullOrWhiteSpace(resource.Path))
        {
            return ValidationProblem("Either rootFolderPath or path is required");
        }

        var added = titles.Add(resource.ToModel(), resource.RootFolderPath ?? "");
        return CreatedAtAction(nameof(Get), new { id = added.Id }, mapper.Title(added, includeIssues: true));
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
    public IActionResult Delete(int id)
    {
        if (titles.Get(id) == null)
        {
            return NotFound();
        }

        titles.Delete(id);
        return Ok();
    }
}
