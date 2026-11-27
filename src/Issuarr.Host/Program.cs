using System.Text.Json.Serialization;
using Issuarr.Core;
using Issuarr.Core.Blocklisting;
using Issuarr.Core.Configuration;
using Issuarr.Core.Datastore;
using Issuarr.Core.Decisions;
using Issuarr.Core.Demo;
using Issuarr.Core.Download;
using Issuarr.Core.DownloadClients;
using Issuarr.Core.HealthCheck;
using Issuarr.Core.History;
using Issuarr.Core.Indexers;
using Issuarr.Core.IndexerSearch;
using Issuarr.Core.Issues;
using Issuarr.Core.Jobs;
using Issuarr.Core.LibraryImport;
using Issuarr.Core.Metadata;
using Issuarr.Core.Notifications;
using Issuarr.Core.Organizer;
using Issuarr.Core.Providers;
using Issuarr.Core.RootFolders;
using Issuarr.Core.Titles;
using Issuarr.Host.Api.V1;
using Issuarr.Host.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace Issuarr.Host;

public class Program
{
    public static DateTime StartTime { get; } = DateTime.UtcNow;
    public static bool IsDemo { get; private set; }

    public static void Main(string[] args)
    {
        var folders = AppFolders.Resolve(args);
        var config = new ConfigFile(folders);
        IsDemo = string.Equals(Environment.GetEnvironmentVariable("ISSUARR_DEMO"), "true", StringComparison.OrdinalIgnoreCase);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args.Where(a => !a.StartsWith("--data=", StringComparison.OrdinalIgnoreCase)).ToArray(),
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "UI"),
        });

        var bind = config.BindAddress == "*" ? "0.0.0.0" : config.BindAddress;
        builder.WebHost.UseUrls($"http://{bind}:{config.Port}");

        var services = builder.Services;
        services.AddSingleton(folders);
        services.AddSingleton(config);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDatabase>(new Database(folders));

        // Library
        services.AddSingleton<ITitleRepository, TitleRepository>();
        services.AddSingleton<IIssueRepository, IssueRepository>();
        services.AddSingleton<IIssueFileRepository, IssueFileRepository>();
        services.AddSingleton<ITitleService, TitleService>();
        services.AddSingleton<IRefreshTitleService, RefreshTitleService>();
        services.AddSingleton<ITitleManagementService, TitleManagementService>();
        services.AddSingleton<IIssueManagementService, IssueManagementService>();
        services.AddSingleton<IRootFolderService, RootFolderService>();
        services.AddSingleton<ILibraryImportService, LibraryImportService>();
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<FileNameBuilder>();
        services.AddSingleton<IHistoryService, HistoryService>();
        services.AddSingleton<IBlocklistService, BlocklistService>();
        services.AddSingleton<DemoDataSeeder>();

        // Providers
        services.AddSingleton<IIndexerRepository, IndexerRepository>();
        services.AddSingleton<IDownloadClientRepository, DownloadClientRepository>();
        services.AddSingleton<INotificationRepository, NotificationRepository>();
        services.AddSingleton<IIndexer, NewznabIndexer>();
        services.AddSingleton<IIndexer, TorznabIndexer>();
        services.AddSingleton<IIndexer, ProwlarrIndexer>();
        services.AddSingleton<TorrentFetcher>();
        services.AddSingleton<IDownloadClient, QBittorrent>();
        services.AddSingleton<IDownloadClient, Deluge>();
        services.AddSingleton<IDownloadClient, Transmission>();
        services.AddSingleton<IDownloadClient, Sabnzbd>();
        services.AddSingleton<IDownloadClient, NzbGet>();
        services.AddSingleton<INotification, Komga>();
        services.AddSingleton<INotification, Kavita>();
        services.AddSingleton<INotification, Discord>();
        services.AddSingleton<INotification, Webhook>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IMetadataProvider, ComicVine>();
        services.AddSingleton<IMetadataProvider, Metron>();
        services.AddSingleton<OpenLibrary>();
        services.AddSingleton<GoogleBooks>();
        services.AddSingleton<IMetadataProvider>(sp => sp.GetRequiredService<OpenLibrary>());
        services.AddSingleton<IMetadataProvider>(sp => sp.GetRequiredService<GoogleBooks>());
        services.AddSingleton<IBookSearch>(sp => sp.GetRequiredService<OpenLibrary>());
        services.AddSingleton<IBookSearch>(sp => sp.GetRequiredService<GoogleBooks>());
        services.AddSingleton<IMetadataProvider, Wikidata>();

        // Searching and downloading
        services.AddSingleton<TrackedDownloadService>();
        services.AddSingleton<ITrackedDownloadService>(sp => sp.GetRequiredService<TrackedDownloadService>());
        services.AddSingleton<IPendingDownloads>(sp => sp.GetRequiredService<TrackedDownloadService>());
        services.AddSingleton<IDecisionMaker, DecisionMaker>();
        services.AddSingleton<IDownloadService, DownloadService>();
        services.AddSingleton<ICompletedDownloadService, CompletedDownloadService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<IHealthCheckService, HealthCheckService>();
        services.AddSingleton<ResourceMapper>();

        // Background jobs
        services.AddSingleton<CommandService>();
        services.AddSingleton<ICommandService>(sp => sp.GetRequiredService<CommandService>());
        services.AddHostedService(sp => sp.GetRequiredService<CommandService>());
        services.AddSingleton<Scheduler>();
        services.AddSingleton<IScheduler>(sp => sp.GetRequiredService<Scheduler>());
        services.AddHostedService(sp => sp.GetRequiredService<Scheduler>());

        var userAgent = $"Issuarr/{SystemController.Version} (+https://github.com/hazarduz/issuarr)";
        services.AddHttpClient(Http.Client, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(60);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        });
        services.AddHttpClient(TorrentFetcher.NoRedirectClient, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(60);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        // Login cookies are encrypted with keys kept in the app data folder so they survive restarts.
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(folders.AppDataFolder, "keys"))).SetApplicationName("Issuarr");
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
        {
            o.Cookie.Name = "IssuarrAuth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.ExpireTimeSpan = TimeSpan.FromDays(30);
            o.SlidingExpiration = true;
            o.LoginPath = "/login";
        });

        services.AddControllers().AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
            o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        var app = builder.Build();
        var log = app.Logger;

        log.LogInformation("Issuarr {Version} starting. AppData: {AppData}", SystemController.Version, folders.AppDataFolder);
        DatabaseMigrator.Migrate(app.Services.GetRequiredService<IDatabase>().ConnectionString);

        if (IsDemo && app.Services.GetRequiredService<DemoDataSeeder>().SeedIfEmpty())
        {
            log.LogInformation("Demo mode: seeded sample library");
        }

        if (config.UrlBase.Length > 0)
        {
            app.UsePathBase(config.UrlBase);
        }

        app.UseAuthentication();
        app.UseMiddleware<AuthGate>();
        app.MapLogin(config);

        // Lets the UI discover where the API lives and which key to use (Sonarr's initialize.json).
        // AuthGate only lets this through for logged-in users (or when login is off).
        app.MapGet("/initialize.json", () => Results.Json(new
        {
            apiRoot = config.UrlBase + "/api/v1",
            apiKey = config.ApiKey,
            urlBase = config.UrlBase,
            instanceName = config.InstanceName,
            version = SystemController.Version,
            isDemo = IsDemo,
            authentication = config.AuthenticationMethod.ToString().ToLowerInvariant(),
        }));

        app.MapGet("/ping", () => Results.Json(new { status = "OK" }));

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapControllers();

        app.MapFallback(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/feed"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var index = Path.Combine(app.Environment.WebRootPath, "index.html");
            if (!File.Exists(index))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("UI not built. Run `npm run build` in frontend/ or use the Vite dev server.");
                return;
            }

            // Make relative asset URLs work under a URL base and on deep links.
            var html = await File.ReadAllTextAsync(index);
            html = html.Replace("<head>", $"<head><base href=\"{config.UrlBase}/\">");
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync(html);
        });

        log.LogInformation("Listening on http://{Bind}:{Port}{UrlBase}", bind, config.Port, config.UrlBase);
        app.Run();
    }
}
