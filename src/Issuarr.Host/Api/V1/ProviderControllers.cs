using Issuarr.Core.DownloadClients;
using Issuarr.Core.Indexers;
using Issuarr.Core.Notifications;
using Issuarr.Core.Providers;
using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.Api.V1;

public record ProviderSchema(string Implementation, string DisplayName, string Description, string? Protocol, IReadOnlyList<ProviderField> Fields);

public record ProviderResource
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Implementation { get; init; } = "";
    public bool Enable { get; init; } = true;
    public int Priority { get; init; } = 25;
    public Dictionary<string, string> Settings { get; init; } = new();
    public string? ImplementationName { get; init; }
    public string? Protocol { get; init; }

    public ProviderDefinition ToModel() => new()
    {
        Id = Id, Name = Name.Trim(), Implementation = Implementation, Enable = Enable, Priority = Priority, Settings = Settings,
    };
}

/// <summary>
/// CRUD + test for one kind of provider. Password fields are never sent back to the browser;
/// saving with the placeholder keeps the stored value.
/// </summary>
public abstract class ProviderControllerBase<TImpl>(IProviderRepository repo, IEnumerable<TImpl> impls) : ControllerBase
    where TImpl : IProvider
{
    private const string Hidden = "********";

    protected abstract string? ProtocolOf(TImpl impl);
    protected abstract Task TestImpl(TImpl impl, ProviderDefinition def, CancellationToken ct);

    [HttpGet]
    public List<ProviderResource> GetAll() => repo.All().Select(ToResource).ToList();

    [HttpGet("{id:int}")]
    public ActionResult<ProviderResource> Get(int id) => repo.Get(id) is { } d ? ToResource(d) : NotFound();

    [HttpGet("schema")]
    public List<ProviderSchema> Schema() =>
        impls.Select(i => new ProviderSchema(i.Implementation, i.DisplayName, i.Description, ProtocolOf(i), i.Fields)).ToList();

    [HttpPost]
    public ActionResult<ProviderResource> Create([FromBody] ProviderResource resource)
    {
        var (def, error) = Validate(resource, null);
        if (error != null)
        {
            return ValidationProblem(error);
        }

        return ToResource(repo.Insert(def!));
    }

    [HttpPut("{id:int}")]
    public ActionResult<ProviderResource> Update(int id, [FromBody] ProviderResource resource)
    {
        var existing = repo.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        var (def, error) = Validate(resource with { Id = id }, existing);
        if (error != null)
        {
            return ValidationProblem(error);
        }

        repo.Update(def!);
        return ToResource(def!);
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        repo.Delete(id);
        return Ok();
    }

    [HttpPost("test")]
    public async Task<IActionResult> Test([FromBody] ProviderResource resource, CancellationToken ct)
    {
        var existing = resource.Id > 0 ? repo.Get(resource.Id) : null;
        var (def, error) = Validate(resource, existing);
        if (error != null)
        {
            return ValidationProblem(error);
        }

        var impl = impls.First(i => i.Implementation == def!.Implementation);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await TestImpl(impl, def!, timeout.Token);
            return Ok(new { message = "Connection OK" });
        }
        catch (Exception e) when (e is ProviderTestException or HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException or UriFormatException)
        {
            var message = e is TaskCanceledException ? "Timed out after 30 seconds" : e is UriFormatException ? "The URL isn't valid" : e.Message;
            return BadRequest(new { message });
        }
    }

    private (ProviderDefinition? Def, string? Error) Validate(ProviderResource resource, ProviderDefinition? existing)
    {
        var impl = impls.FirstOrDefault(i => i.Implementation == resource.Implementation);
        if (impl == null)
        {
            return (null, $"Unknown type: {resource.Implementation}");
        }

        var def = resource.ToModel();
        if (string.IsNullOrWhiteSpace(def.Name))
        {
            def.Name = impl.DisplayName;
        }

        // Keep stored secrets when the UI sends back the placeholder.
        foreach (var f in impl.Fields.Where(f => f.Type == FieldType.Password))
        {
            if (def.Settings.GetValueOrDefault(f.Name) == Hidden && existing != null)
            {
                def.Settings[f.Name] = existing.Settings.GetValueOrDefault(f.Name) ?? "";
            }
        }

        var missing = impl.Fields.Where(f => f.Required && string.IsNullOrWhiteSpace(def.Settings.GetValueOrDefault(f.Name))).Select(f => f.Label).ToList();
        return missing.Count > 0 ? (null, $"Required: {string.Join(", ", missing)}") : (def, null);
    }

    private ProviderResource ToResource(ProviderDefinition d)
    {
        var impl = impls.FirstOrDefault(i => i.Implementation == d.Implementation);
        var secrets = impl?.Fields.Where(f => f.Type == FieldType.Password).Select(f => f.Name).ToHashSet() ?? [];
        return new ProviderResource
        {
            Id = d.Id,
            Name = d.Name,
            Implementation = d.Implementation,
            Enable = d.Enable,
            Priority = d.Priority,
            Settings = d.Settings.ToDictionary(kv => kv.Key, kv => secrets.Contains(kv.Key) && kv.Value.Length > 0 ? Hidden : kv.Value),
            ImplementationName = impl?.DisplayName ?? d.Implementation,
            Protocol = impl == null ? null : ProtocolOf(impl),
        };
    }
}

[ApiController]
[Route("api/v1/indexer")]
public class IndexerController(IIndexerRepository repo, IEnumerable<IIndexer> impls) : ProviderControllerBase<IIndexer>(repo, impls)
{
    protected override string? ProtocolOf(IIndexer impl) => impl.Protocol == DownloadProtocol.Unknown ? "both" : impl.Protocol.ToString().ToLowerInvariant();
    protected override Task TestImpl(IIndexer impl, ProviderDefinition def, CancellationToken ct) => impl.Test(def, ct);
}

[ApiController]
[Route("api/v1/downloadclient")]
public class DownloadClientController(IDownloadClientRepository repo, IEnumerable<IDownloadClient> impls) : ProviderControllerBase<IDownloadClient>(repo, impls)
{
    protected override string? ProtocolOf(IDownloadClient impl) => impl.Protocol.ToString().ToLowerInvariant();
    protected override Task TestImpl(IDownloadClient impl, ProviderDefinition def, CancellationToken ct) => impl.Test(def, ct);
}

[ApiController]
[Route("api/v1/notification")]
public class NotificationController(INotificationRepository repo, IEnumerable<INotification> impls) : ProviderControllerBase<INotification>(repo, impls)
{
    protected override string? ProtocolOf(INotification impl) => null;
    protected override Task TestImpl(INotification impl, ProviderDefinition def, CancellationToken ct) => impl.Test(def, ct);
}
