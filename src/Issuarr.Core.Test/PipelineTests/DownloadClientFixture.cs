using System.Net;
using Issuarr.Core.DownloadClients;
using Issuarr.Core.Indexers;
using Issuarr.Core.Providers;

namespace Issuarr.Core.Test.PipelineTests;

[TestFixture]
public class DownloadClientFixture
{
    private const string Magnet = "magnet:?xt=urn:btih:ABCDEF0123456789ABCDEF0123456789ABCDEF01&dn=Iron+Tide+046";

    [Test]
    public async Task qbittorrent_should_login_add_with_category_and_map_queue()
    {
        var http = new FakeHttp();
        http.Respond = (req, body) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/auth/login"))
            {
                var res = FakeHttp.Text("Ok.");
                res.Headers.Add("Set-Cookie", "SID=abc123; HttpOnly; path=/");
                return res;
            }

            Assert.That(req.Headers.GetValues("Cookie").Single(), Is.EqualTo("SID=abc123"), $"cookie sent to {path}");
            return path switch
            {
                var p when p.EndsWith("/torrents/createCategory") => FakeHttp.Text("", HttpStatusCode.Conflict),
                var p when p.EndsWith("/torrents/add") => FakeHttp.Text("Ok."),
                var p when p.EndsWith("/torrents/info") => FakeHttp.Text("""
                    [{"hash":"ABCDEF0123456789ABCDEF0123456789ABCDEF01","name":"Iron Tide 046","size":1000,"amount_left":250,"eta":60,"state":"downloading","content_path":"/downloads/issuarr/Iron Tide 046","save_path":"/downloads/issuarr"},
                     {"hash":"ff","name":"Done","size":10,"amount_left":0,"eta":8640000,"state":"stalledUP","content_path":"/downloads/issuarr/Done"}]
                    """),
                _ => FakeHttp.Text("", HttpStatusCode.NotFound),
            };
        };

        var factory = new FakeHttpFactory(http);
        var client = new QBittorrent(factory, new TorrentFetcher(factory));
        var def = new ProviderDefinition
        {
            Id = 901, Name = "qBit",
            Settings = new() { ["host"] = "qbit", ["port"] = "8080", ["username"] = "admin", ["password"] = "pw", ["category"] = "issuarr", ["remotePath"] = "/downloads", ["localPath"] = "/data/torrents" },
        };

        var hash = await client.Add(def, new ReleaseInfo { Title = "Iron Tide 046", DownloadUrl = Magnet, Protocol = DownloadProtocol.Torrent }, CancellationToken.None);
        var items = await client.GetItems(def, CancellationToken.None);

        Assert.That(hash, Is.EqualTo("abcdef0123456789abcdef0123456789abcdef01"));
        var add = http.Requests.Single(r => r.Url.EndsWith("/torrents/add"));
        Assert.That(add.Body, Does.Contain("issuarr").And.Contain("magnet:?xt=urn:btih:ABCDEF"));
        Assert.That(http.Requests.Single(r => r.Url.Contains("/torrents/info")).Url, Does.Contain("category=issuarr"));

        Assert.That(items[0].Status, Is.EqualTo(DownloadItemStatus.Downloading));
        Assert.That(items[0].RemainingSize, Is.EqualTo(250));
        Assert.That(items[0].Eta, Is.EqualTo(TimeSpan.FromSeconds(60)));
        Assert.That(items[0].OutputPath, Is.EqualTo("/data/torrents/issuarr/Iron Tide 046"), "remote path mapping applied");
        Assert.That(items[1].Status, Is.EqualTo(DownloadItemStatus.Completed));
        Assert.That(items[1].Eta, Is.Null);
    }

    [Test]
    public async Task sabnzbd_should_add_url_and_merge_queue_and_history()
    {
        var http = new FakeHttp
        {
            Respond = (req, _) =>
            {
                var q = req.RequestUri!.Query;
                Assert.That(q, Does.Contain("apikey=KEY"));
                if (q.Contains("mode=addurl"))
                {
                    return FakeHttp.Text("""{"status":true,"nzo_ids":["SABnzbd_nzo_1"]}""");
                }

                if (q.Contains("mode=queue"))
                {
                    return FakeHttp.Text("""{"queue":{"slots":[{"nzo_id":"SABnzbd_nzo_1","filename":"Circuit and Solder Nov 2026","mb":"100.0","mbleft":"40.0","timeleft":"0:02:30","status":"Downloading","cat":"issuarr"},{"nzo_id":"other","filename":"Linux ISO","mb":"1","mbleft":"1","timeleft":"0:00:00","status":"Queued","cat":"tv"}]}}""");
                }

                return FakeHttp.Text("""{"history":{"slots":[{"nzo_id":"SABnzbd_nzo_0","name":"Velocity Oct 2026","bytes":5000,"status":"Completed","storage":"/downloads/complete/issuarr/Velocity Oct 2026","category":"issuarr"},{"nzo_id":"SABnzbd_nzo_9","name":"Bad","bytes":1,"status":"Failed","fail_message":"Out of retention","category":"issuarr"}]}}""");
            },
        };

        var client = new Sabnzbd(new FakeHttpFactory(http));
        var def = new ProviderDefinition { Name = "SAB", Settings = new() { ["host"] = "sab", ["apiKey"] = "KEY", ["category"] = "issuarr" } };

        var id = await client.Add(def, new ReleaseInfo { Title = "Circuit and Solder Nov 2026", DownloadUrl = "https://indexer/get/1?apikey=x", Protocol = DownloadProtocol.Usenet }, CancellationToken.None);
        var items = await client.GetItems(def, CancellationToken.None);

        Assert.That(id, Is.EqualTo("SABnzbd_nzo_1"));
        Assert.That(http.Requests[0].Url, Does.Contain("name=https%3A%2F%2Findexer%2Fget%2F1%3Fapikey%3Dx").And.Contain("cat=issuarr"));
        Assert.That(items.Select(i => i.DownloadId), Is.EqualTo(new[] { "SABnzbd_nzo_1", "SABnzbd_nzo_0", "SABnzbd_nzo_9" }), "other categories ignored");
        Assert.That(items[0].RemainingSize, Is.EqualTo(40L * 1024 * 1024));
        Assert.That(items[0].Eta, Is.EqualTo(TimeSpan.FromSeconds(150)));
        Assert.That(items[1].Status, Is.EqualTo(DownloadItemStatus.Completed));
        Assert.That(items[1].OutputPath, Is.EqualTo("/downloads/complete/issuarr/Velocity Oct 2026"));
        Assert.That(items[2].Status, Is.EqualTo(DownloadItemStatus.Failed));
        Assert.That(items[2].Message, Is.EqualTo("Out of retention"));
    }

    [Test]
    public async Task transmission_should_negotiate_session_id()
    {
        var calls = 0;
        var http = new FakeHttp
        {
            Respond = (req, body) =>
            {
                if (calls++ == 0)
                {
                    var conflict = FakeHttp.Text("", HttpStatusCode.Conflict);
                    conflict.Headers.Add("X-Transmission-Session-Id", "sess-1");
                    return conflict;
                }

                Assert.That(req.Headers.GetValues("X-Transmission-Session-Id").Single(), Is.EqualTo("sess-1"));
                return FakeHttp.Text("""{"result":"success","arguments":{"torrents":[{"hashString":"AA","name":"Neon Alley 013","totalSize":100,"leftUntilDone":0,"percentDone":1,"status":6,"error":0,"downloadDir":"/downloads/issuarr","labels":[]},{"hashString":"BB","name":"Other","totalSize":1,"leftUntilDone":1,"percentDone":0,"status":4,"error":0,"downloadDir":"/downloads/tv","labels":[]}]}}""");
            },
        };

        var client = new Transmission(new FakeHttpFactory(http), new TorrentFetcher(new FakeHttpFactory(http)));
        var items = await client.GetItems(new ProviderDefinition { Settings = new() { ["host"] = "tr", ["category"] = "issuarr" } }, CancellationToken.None);

        Assert.That(items.Single().DownloadId, Is.EqualTo("aa"));
        Assert.That(items.Single().Status, Is.EqualTo(DownloadItemStatus.Completed));
        Assert.That(items.Single().OutputPath, Is.EqualTo("/downloads/issuarr/Neon Alley 013"));
    }
}
