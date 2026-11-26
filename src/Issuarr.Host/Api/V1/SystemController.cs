using System.Reflection;
using Issuarr.Core.Configuration;
using Issuarr.Core.Parser;
using Issuarr.Core.RootFolders;
using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.Api.V1;

public record HealthResource(string Source, string Type, string Message);

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
public class HealthController(IRootFolderService rootFolders, ConfigFile config) : ControllerBase
{
    [HttpGet]
    public List<HealthResource> Get()
    {
        var results = new List<HealthResource>();
        var folders = rootFolders.All();
        if (folders.Count == 0)
        {
            results.Add(new("RootFolderCheck", "warning", "No root folders configured. Add one in Settings → Media Management."));
        }

        results.AddRange(folders.Where(f => !f.Accessible)
            .Select(f => new HealthResource("RootFolderCheck", "error", $"Root folder is missing or not accessible: {f.Folder.Path}")));

        results.Add(new("IndexerCheck", "warning", "No indexers are configured, so Issuarr cannot search for releases yet."));
        results.Add(new("DownloadClientCheck", "warning", "No download client is configured."));

        if (config.AuthenticationMethod == AuthenticationMethod.None)
        {
            results.Add(new("AuthenticationCheck", "notice", "Authentication is disabled. Don't expose Issuarr to the internet without it."));
        }

        return results;
    }
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

[ApiController]
[Route("api/v1")]
public class ActivityController : ControllerBase
{
    // Download clients aren't wired up yet; these exist so the UI has a stable contract.
    [HttpGet("queue")]
    public object Queue() => new { totalRecords = 0, records = Array.Empty<object>() };

    [HttpGet("history")]
    public object History() => new { totalRecords = 0, records = Array.Empty<object>() };
}
