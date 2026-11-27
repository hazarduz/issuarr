using System.Globalization;
using Dapper;
using Issuarr.Core.Datastore;

namespace Issuarr.Core.Providers;

public enum FieldType
{
    Text,
    Password,
    Number,
    Checkbox,
    Select,
    Url,
}

/// <summary>Describes one setting of a provider so the UI can render a form for it.</summary>
public record ProviderField(
    string Name,
    string Label,
    FieldType Type = FieldType.Text,
    string? HelpText = null,
    string? Default = null,
    bool Required = false,
    string[]? Options = null,
    bool Advanced = false);

public class ProviderDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Implementation { get; set; } = "";
    public bool Enable { get; set; } = true;
    public int Priority { get; set; } = 25;
    public Dictionary<string, string> Settings { get; set; } = new();

    public string Get(string key, string fallback = "") =>
        Settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

    public int GetInt(string key, int fallback) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;

    public bool GetBool(string key, bool fallback = false) => bool.TryParse(Get(key), out var b) ? b : fallback;
}

public interface IProvider
{
    string Implementation { get; }
    string DisplayName { get; }
    string Description { get; }
    IReadOnlyList<ProviderField> Fields { get; }
}

public class ProviderTestException(string message) : Exception(message);

public interface IProviderRepository
{
    List<ProviderDefinition> All();
    ProviderDefinition? Get(int id);
    ProviderDefinition Insert(ProviderDefinition def);
    void Update(ProviderDefinition def);
    void Delete(int id);
}

public abstract class ProviderRepository(IDatabase db, string table) : IProviderRepository
{
    public List<ProviderDefinition> All()
    {
        using var conn = db.Open();
        return conn.Query<ProviderDefinition>($"SELECT * FROM {table} ORDER BY Priority, Name").AsList();
    }

    public ProviderDefinition? Get(int id)
    {
        using var conn = db.Open();
        return conn.QuerySingleOrDefault<ProviderDefinition>($"SELECT * FROM {table} WHERE Id = @id", new { id });
    }

    public ProviderDefinition Insert(ProviderDefinition def)
    {
        using var conn = db.Open();
        def.Id = conn.ExecuteScalar<int>(
            $"INSERT INTO {table} (Name, Implementation, Enable, Priority, Settings) VALUES (@Name, @Implementation, @Enable, @Priority, @Settings); SELECT last_insert_rowid();",
            def);
        return def;
    }

    public void Update(ProviderDefinition def)
    {
        using var conn = db.Open();
        conn.Execute($"UPDATE {table} SET Name = @Name, Implementation = @Implementation, Enable = @Enable, Priority = @Priority, Settings = @Settings WHERE Id = @Id", def);
    }

    public void Delete(int id)
    {
        using var conn = db.Open();
        conn.Execute($"DELETE FROM {table} WHERE Id = @id", new { id });
    }
}

public interface IIndexerRepository : IProviderRepository;
public class IndexerRepository(IDatabase db) : ProviderRepository(db, "Indexers"), IIndexerRepository;

public interface IDownloadClientRepository : IProviderRepository;
public class DownloadClientRepository(IDatabase db) : ProviderRepository(db, "DownloadClients"), IDownloadClientRepository;

public interface INotificationRepository : IProviderRepository;
public class NotificationRepository(IDatabase db) : ProviderRepository(db, "Notifications"), INotificationRepository;

public static class Http
{
    /// <summary>Name of the shared HttpClient (sets a User-Agent; ComicVine rejects requests without one).</summary>
    public const string Client = "issuarr";

    public static string Combine(string baseUrl, string path) => baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');

    public static async Task<string> ReadOrThrow(HttpResponseMessage res, string what, CancellationToken ct)
    {
        var body = await res.Content.ReadAsStringAsync(ct);
        if (res.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            throw new ProviderTestException($"{what}: authentication failed (HTTP {(int)res.StatusCode}). Check the API key / username / password.");
        }

        if (!res.IsSuccessStatusCode)
        {
            throw new ProviderTestException($"{what}: HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
        }

        return body;
    }
}
