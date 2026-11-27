using Issuarr.Core.Configuration;
using Issuarr.Core.Download;
using Issuarr.Core.Providers;
using Issuarr.Core.RootFolders;

namespace Issuarr.Core.HealthCheck;

public record HealthResult(string Source, string Type, string Message, string? Link = null);

public interface IHealthCheckService
{
    List<HealthResult> Check();
}

public class HealthCheckService(
    IRootFolderService rootFolders,
    IIndexerRepository indexers,
    IDownloadClientRepository clients,
    ITrackedDownloadService tracked,
    IConfigService config,
    ConfigFile configFile) : IHealthCheckService
{
    public List<HealthResult> Check()
    {
        var results = new List<HealthResult>();
        var folders = rootFolders.All();
        if (folders.Count == 0)
        {
            results.Add(new("RootFolderCheck", "warning", "No root folders. Add one in Settings → Media Management.", "/settings/media"));
        }

        results.AddRange(folders.Where(f => !f.Accessible).Select(f =>
            new HealthResult("RootFolderCheck", "error", $"Root folder is missing or not accessible: {f.Folder.Path}", "/settings/media")));

        if (!indexers.All().Any(i => i.Enable))
        {
            results.Add(new("IndexerCheck", "warning", "No indexers are enabled, so Issuarr can't search for releases.", "/settings/indexers"));
        }

        var enabledClients = clients.All().Where(c => c.Enable).ToList();
        if (enabledClients.Count == 0)
        {
            results.Add(new("DownloadClientCheck", "warning", "No download client is enabled.", "/settings/downloadclients"));
        }

        if (tracked is TrackedDownloadService t)
        {
            foreach (var (id, error) in t.ClientErrors)
            {
                var name = enabledClients.FirstOrDefault(c => c.Id == id)?.Name ?? "Download client";
                results.Add(new("DownloadClientCheck", "error", $"{name} is unavailable: {error}", "/settings/downloadclients"));
            }
        }

        foreach (var d in tracked.All().Where(d => d.State == TrackedState.ImportFailed))
        {
            results.Add(new("ImportCheck", "warning", $"Couldn't import \"{d.Item.Title}\": {string.Join(" ", d.StatusMessages)}", "/activity"));
        }

        var source = config.MetadataSource;
        if (source == "comicvine" && config.ComicVineApiKey.Length == 0)
        {
            results.Add(new("MetadataCheck", "warning", "Add a ComicVine API key (free) in Settings → General to search for comics.", "/settings/general"));
        }
        else if (source == "metron" && (config.MetronUsername.Length == 0 || config.MetronPassword.Length == 0))
        {
            results.Add(new("MetadataCheck", "warning", "Add your Metron username and password in Settings → General to search for comics.", "/settings/general"));
        }

        if (configFile.AuthenticationMethod == AuthenticationMethod.None)
        {
            results.Add(new("AuthenticationCheck", "notice", "Login is turned off. Turn it on in Settings → General before exposing Issuarr outside your network.", "/settings/general"));
        }

        return results;
    }
}
