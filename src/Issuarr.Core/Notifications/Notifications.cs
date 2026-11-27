using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Issuarr.Core.Issues;
using Issuarr.Core.Providers;
using Issuarr.Core.Titles;
using Microsoft.Extensions.Logging;

namespace Issuarr.Core.Notifications;

public record GrabMessage(Title Title, List<Issue> Issues, string ReleaseTitle, string Indexer, string DownloadClient, long Size);

public record ImportMessage(Title Title, Issue Issue, string Path, bool IsUpgrade);

public interface INotification : IProvider
{
    Task OnGrab(ProviderDefinition def, GrabMessage message, CancellationToken ct);
    Task OnImport(ProviderDefinition def, ImportMessage message, CancellationToken ct);
    Task Test(ProviderDefinition def, CancellationToken ct);
}

public abstract class NotificationBase(IHttpClientFactory http) : INotification
{
    protected IHttpClientFactory HttpFactory { get; } = http;
    public abstract string Implementation { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    protected abstract IEnumerable<ProviderField> ConnectionFields { get; }
    protected virtual bool SupportsGrab => true;

    public IReadOnlyList<ProviderField> Fields =>
    [
        .. ConnectionFields,
        .. SupportsGrab ? [new ProviderField("onGrab", "On Grab", FieldType.Checkbox, "When a release is sent to a download client", "true")] : Array.Empty<ProviderField>(),
        new("onImport", SupportsGrab ? "On Import" : "Scan on Import", FieldType.Checkbox, "When an issue is imported into the library", "true"),
    ];

    public virtual Task OnGrab(ProviderDefinition def, GrabMessage message, CancellationToken ct) => Task.CompletedTask;
    public abstract Task OnImport(ProviderDefinition def, ImportMessage message, CancellationToken ct);
    public abstract Task Test(ProviderDefinition def, CancellationToken ct);

    protected static string Issues(IEnumerable<Issue> issues) => string.Join(", ", issues.Select(i => i.Number));
}

public class Discord(IHttpClientFactory http) : NotificationBase(http)
{
    public override string Implementation => "Discord";
    public override string DisplayName => "Discord";
    public override string Description => "Post to a Discord channel with a webhook";
    protected override IEnumerable<ProviderField> ConnectionFields =>
    [
        new("webhookUrl", "Webhook URL", FieldType.Url, "Channel settings → Integrations → Webhooks", Required: true),
        new("username", "Username", HelpText: "Name the messages are posted as", Default: "Issuarr"),
    ];

    public override Task OnGrab(ProviderDefinition def, GrabMessage m, CancellationToken ct) =>
        def.GetBool("onGrab", true) ? Post(def, $"Grabbed: {m.Title.Name} {Issues(m.Issues)}", m.ReleaseTitle, 0x5aa9ff,
            [("Indexer", m.Indexer), ("Download client", m.DownloadClient)], ct) : Task.CompletedTask;

    public override Task OnImport(ProviderDefinition def, ImportMessage m, CancellationToken ct) =>
        def.GetBool("onImport", true) ? Post(def, $"{(m.IsUpgrade ? "Upgraded" : "Imported")}: {m.Title.Name} {m.Issue.Number}", Path.GetFileName(m.Path), 0x3ecf8e, [], ct) : Task.CompletedTask;

    public override Task Test(ProviderDefinition def, CancellationToken ct) => Post(def, "Issuarr test", "If you can see this, Discord notifications work.", 0xff8a4c, [], ct);

    private async Task Post(ProviderDefinition def, string title, string description, int color, (string Name, string Value)[] fields, CancellationToken ct)
    {
        var payload = new JsonObject
        {
            ["username"] = def.Get("username", "Issuarr"),
            ["embeds"] = new JsonArray(new JsonObject
            {
                ["title"] = title,
                ["description"] = description,
                ["color"] = color,
                ["fields"] = new JsonArray(fields.Select(f => (JsonNode)new JsonObject { ["name"] = f.Name, ["value"] = f.Value, ["inline"] = true }).ToArray()),
            }),
        };
        using var res = await HttpFactory.CreateClient(Http.Client).PostAsync(def.Get("webhookUrl"), new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"), ct);
        await Http.ReadOrThrow(res, "Discord", ct);
    }
}

public class Webhook(IHttpClientFactory http) : NotificationBase(http)
{
    public override string Implementation => "Webhook";
    public override string DisplayName => "Webhook";
    public override string Description => "POST a JSON message to any URL (Home Assistant, n8n, Apprise…)";
    protected override IEnumerable<ProviderField> ConnectionFields => [new("url", "URL", FieldType.Url, Required: true)];

    public override Task OnGrab(ProviderDefinition def, GrabMessage m, CancellationToken ct) =>
        def.GetBool("onGrab", true) ? Post(def, new
        {
            eventType = "Grab",
            title = new { m.Title.Id, m.Title.Name, kind = m.Title.Kind.ToString(), m.Title.Path },
            issues = m.Issues.Select(i => new { i.Id, i.Number, i.ReleaseDate }),
            release = new { title = m.ReleaseTitle, indexer = m.Indexer, size = m.Size, downloadClient = m.DownloadClient },
        }, ct) : Task.CompletedTask;

    public override Task OnImport(ProviderDefinition def, ImportMessage m, CancellationToken ct) =>
        def.GetBool("onImport", true) ? Post(def, new
        {
            eventType = m.IsUpgrade ? "Upgrade" : "Download",
            title = new { m.Title.Id, m.Title.Name, kind = m.Title.Kind.ToString(), m.Title.Path },
            issue = new { m.Issue.Id, m.Issue.Number, m.Issue.ReleaseDate },
            path = m.Path,
        }, ct) : Task.CompletedTask;

    public override Task Test(ProviderDefinition def, CancellationToken ct) => Post(def, new { eventType = "Test" }, ct);

    private async Task Post(ProviderDefinition def, object payload, CancellationToken ct)
    {
        using var res = await HttpFactory.CreateClient(Http.Client).PostAsJsonAsync(def.Get("url"), payload, ct);
        await Http.ReadOrThrow(res, "Webhook", ct);
    }
}

/// <summary>Asks Komga to rescan its libraries when something is imported.</summary>
public class Komga(IHttpClientFactory http) : NotificationBase(http)
{
    public override string Implementation => "Komga";
    public override string DisplayName => "Komga";
    public override string Description => "Rescan Komga libraries when issues are imported";
    protected override bool SupportsGrab => false;
    protected override IEnumerable<ProviderField> ConnectionFields =>
    [
        new("url", "Komga URL", FieldType.Url, "e.g. http://komga:25600", "http://localhost:25600", Required: true),
        new("apiKey", "API Key", FieldType.Password, "Komga → Account settings → API Keys (Komga 1.20+). Or use username and password instead."),
        new("username", "Username (email)"),
        new("password", "Password", FieldType.Password),
        new("libraryIds", "Library IDs", HelpText: "Optional, comma separated. Leave empty to scan every library."),
    ];

    public override async Task OnImport(ProviderDefinition def, ImportMessage m, CancellationToken ct)
    {
        if (!def.GetBool("onImport", true))
        {
            return;
        }

        foreach (var id in await LibraryIds(def, ct))
        {
            using var req = Request(def, HttpMethod.Post, $"api/v1/libraries/{id}/scan");
            using var res = await HttpFactory.CreateClient(Http.Client).SendAsync(req, ct);
            await Http.ReadOrThrow(res, "Komga", ct);
        }
    }

    public override async Task Test(ProviderDefinition def, CancellationToken ct)
    {
        if ((await LibraryIds(def, ct)).Count == 0)
        {
            throw new ProviderTestException("Connected to Komga, but it has no libraries");
        }
    }

    private async Task<List<string>> LibraryIds(ProviderDefinition def, CancellationToken ct)
    {
        var configured = def.Get("libraryIds").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (configured.Count > 0)
        {
            return configured;
        }

        using var req = Request(def, HttpMethod.Get, "api/v1/libraries");
        using var res = await HttpFactory.CreateClient(Http.Client).SendAsync(req, ct);
        var libs = JsonNode.Parse(await Http.ReadOrThrow(res, "Komga", ct))!.AsArray();
        return libs.Select(l => (string?)l!["id"]).OfType<string>().ToList();
    }

    private static HttpRequestMessage Request(ProviderDefinition def, HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, Http.Combine(def.Get("url"), path));
        if (def.Get("apiKey").Length > 0)
        {
            req.Headers.Add("X-API-Key", def.Get("apiKey"));
        }
        else
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{def.Get("username")}:{def.Get("password")}")));
        }

        return req;
    }
}

/// <summary>Asks Kavita to rescan when something is imported.</summary>
public class Kavita(IHttpClientFactory http) : NotificationBase(http)
{
    public override string Implementation => "Kavita";
    public override string DisplayName => "Kavita";
    public override string Description => "Rescan Kavita libraries when issues are imported";
    protected override bool SupportsGrab => false;
    protected override IEnumerable<ProviderField> ConnectionFields =>
    [
        new("url", "Kavita URL", FieldType.Url, "e.g. http://kavita:5000", "http://localhost:5000", Required: true),
        new("apiKey", "API Key", FieldType.Password, "Kavita → User settings → 3rd Party Clients → API Key", Required: true),
        new("libraryIds", "Library IDs", HelpText: "Optional, comma separated. Leave empty to scan every library."),
    ];

    public override async Task OnImport(ProviderDefinition def, ImportMessage m, CancellationToken ct)
    {
        if (!def.GetBool("onImport", true))
        {
            return;
        }

        var token = await Authenticate(def, ct);
        var ids = def.Get("libraryIds").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var paths = ids.Length == 0 ? ["api/Library/scan-all"] : ids.Select(id => $"api/Library/scan?libraryId={Uri.EscapeDataString(id)}").ToArray();
        foreach (var path in paths)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Http.Combine(def.Get("url"), path));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await HttpFactory.CreateClient(Http.Client).SendAsync(req, ct);
            await Http.ReadOrThrow(res, "Kavita", ct);
        }
    }

    public override Task Test(ProviderDefinition def, CancellationToken ct) => Authenticate(def, ct);

    private async Task<string> Authenticate(ProviderDefinition def, CancellationToken ct)
    {
        var url = Http.Combine(def.Get("url"), $"api/Plugin/authenticate?apiKey={Uri.EscapeDataString(def.Get("apiKey"))}&pluginName=Issuarr");
        using var res = await HttpFactory.CreateClient(Http.Client).PostAsync(url, null, ct);
        var body = JsonNode.Parse(await Http.ReadOrThrow(res, "Kavita", ct));
        return (string?)body?["token"] ?? throw new ProviderTestException("Kavita didn't return a token. Check the API key.");
    }
}

public interface INotificationService
{
    void Grabbed(GrabMessage message);
    void Imported(ImportMessage message);
}

/// <summary>Sends events to every enabled connection in the background; failures are logged, never thrown.</summary>
public class NotificationService(INotificationRepository repo, IEnumerable<INotification> impls, ILogger<NotificationService> log) : INotificationService
{
    public void Grabbed(GrabMessage message) => Fire((n, d) => n.OnGrab(d, message, CancellationToken.None));

    public void Imported(ImportMessage message) => Fire((n, d) => n.OnImport(d, message, CancellationToken.None));

    private void Fire(Func<INotification, ProviderDefinition, Task> send)
    {
        foreach (var def in repo.All().Where(d => d.Enable))
        {
            var impl = impls.FirstOrDefault(i => i.Implementation == def.Implementation);
            if (impl == null)
            {
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await send(impl, def);
                }
                catch (Exception e)
                {
                    log.LogWarning("Notification {Name} failed: {Message}", def.Name, e.Message);
                }
            });
        }
    }
}
