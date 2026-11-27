using System.Reflection;
using Issuarr.Core.Configuration;
using Issuarr.Core.HealthCheck;
using Issuarr.Core.Jobs;
using Issuarr.Core.Parser;
using Issuarr.Core.RootFolders;
using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.Api.V1;

public record RootFolderResource(int Id, string Path, long? FreeSpace, bool Accessible);

[ApiController]
[Route("api/v1/system")]
public class SystemController(AppFolders folders, ConfigFile config, IWebHostEnvironment env) : ControllerBase
{
    public static string Version =>
        typeof(SystemController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    [HttpGet("status")]
    public object Status() => new
    {
        appName = "Issuarr",
        instanceName = config.InstanceName,
        version = Version,
        startTime = Program.StartTime,
        appData = folders.AppDataFolder,
        databaseType = "sqLite",
        urlBase = config.UrlBase,
        authentication = config.AuthenticationMethod.ToString().ToLowerInvariant(),
        runtimeVersion = Environment.Version.ToString(),
        osName = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        isDocker = System.IO.File.Exists("/.dockerenv"),
        isDemo = Program.IsDemo,
        environment = env.EnvironmentName,
    };
}

[ApiController]
[Route("api/v1/health")]
public class HealthController(IHealthCheckService health) : ControllerBase
{
    [HttpGet]
    public List<HealthResult> Get() => health.Check();
}

[ApiController]
[Route("api/v1/system/task")]
public class TaskController(IScheduler scheduler) : ControllerBase
{
    [HttpGet]
    public object Get() => scheduler.Tasks().Select(t => new { t.Name, t.Label, interval = (int)t.Interval.TotalMinutes, t.LastRun, t.NextRun });
}

[ApiController]
[Route("api/v1/rootfolder")]
public class RootFolderController(IRootFolderService rootFolders) : ControllerBase
{
    [HttpGet]
    public List<RootFolderResource> Get() =>
        rootFolders.All().Select(f => new RootFolderResource(f.Folder.Id, f.Folder.Path, f.FreeSpace, f.Accessible)).ToList();

    [HttpPost]
    public ActionResult<RootFolderResource> Add([FromBody] RootFolderResource resource)
    {
        try
        {
            var folder = rootFolders.Add(resource.Path);
            return new RootFolderResource(folder.Id, folder.Path, null, Directory.Exists(folder.Path));
        }
        catch (ArgumentException e)
        {
            return ValidationProblem(e.Message);
        }
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        if (rootFolders.All().All(f => f.Folder.Id != id))
        {
            return NotFound();
        }

        // Titles keep their own paths, so removing a root folder never touches files or titles.
        rootFolders.Remove(id);
        return Ok();
    }
}

[ApiController]
[Route("api/v1/parse")]
public class ParseController : ControllerBase
{
    /// <summary>Debug endpoint: shows how a release name would be understood.</summary>
    [HttpGet]
    public ActionResult<ParsedRelease> Parse([FromQuery] string title)
    {
        var parsed = ReleaseParser.Parse(title);
        return parsed == null ? NotFound() : parsed;
    }
}
