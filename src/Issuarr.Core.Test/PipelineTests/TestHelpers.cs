using System.Net;
using Issuarr.Core.Configuration;
using Issuarr.Core.Datastore;

namespace Issuarr.Core.Test.PipelineTests;

/// <summary>Fake HTTP: map "METHOD path-fragment" to a response.</summary>
public class FakeHttp : HttpMessageHandler
{
    public List<(HttpMethod Method, string Url, string Body)> Requests { get; } = [];
    public Func<HttpRequestMessage, string, HttpResponseMessage> Respond { get; set; } = (_, _) => new HttpResponseMessage(HttpStatusCode.NotFound);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request.Method, request.RequestUri!.AbsoluteUri, body));
        return Respond(request, body);
    }

    public static HttpResponseMessage Text(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body) };
}

public class FakeHttpFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

public sealed class TempDb : IDisposable
{
    public TempDb()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"issuarr-test-{Guid.NewGuid():N}.db");
        Db = new Database($"Data Source={Path};Foreign Keys=True");
        DatabaseMigrator.Migrate(Db.ConnectionString);
    }

    public string Path { get; }
    public Database Db { get; }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(Path);
    }
}
