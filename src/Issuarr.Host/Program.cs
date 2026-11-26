using System.Text.Json.Serialization;
using Issuarr.Core;
using Issuarr.Core.Configuration;
using Issuarr.Core.Datastore;
using Issuarr.Core.Demo;
using Issuarr.Core.Issues;
using Issuarr.Core.LibraryImport;
using Issuarr.Core.RootFolders;
using Issuarr.Core.Titles;
using Issuarr.Host.Api.V1;
using Issuarr.Host.Http;

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

        builder.Services.AddSingleton(folders);
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IDatabase>(new Database(folders));
        builder.Services.AddSingleton<ITitleRepository, TitleRepository>();
        builder.Services.AddSingleton<IIssueRepository, IssueRepository>();
        builder.Services.AddSingleton<IIssueFileRepository, IssueFileRepository>();
        builder.Services.AddSingleton<ITitleService, TitleService>();
        builder.Services.AddSingleton<IRootFolderService, RootFolderService>();
        builder.Services.AddSingleton<DemoDataSeeder>();
        builder.Services.AddSingleton<ILibraryImportService, LibraryImportService>();
        builder.Services.AddSingleton<ResourceMapper>();

        builder.Services.AddControllers().AddJsonOptions(o =>
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

        // Lets the UI discover where the API lives and which key to use, like Sonarr's initialize.json.
        // NOTE: until login is implemented this is readable by anyone who can reach the web UI.
        app.MapGet("/initialize.json", () => Results.Json(new
        {
            apiRoot = config.UrlBase + "/api/v1",
            apiKey = config.ApiKey,
            urlBase = config.UrlBase,
            instanceName = config.InstanceName,
            version = SystemController.Version,
            isDemo = IsDemo,
        }));

        app.MapGet("/ping", () => Results.Json(new { status = "OK" }));

        app.UseMiddleware<ApiKeyMiddleware>();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapControllers();

        // Anything else that isn't the API is a client-side route; hand it to the SPA.
        app.MapFallback(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
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

            context.Response.ContentType = "text/html";
            await context.Response.SendFileAsync(index);
        });

        log.LogInformation("Listening on http://{Bind}:{Port}{UrlBase}", bind, config.Port, config.UrlBase);
        app.Run();
    }
}
