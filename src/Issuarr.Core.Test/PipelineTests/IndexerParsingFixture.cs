using System.Text;
using System.Xml.Linq;
using Issuarr.Core.DownloadClients;
using Issuarr.Core.Indexers;
using Issuarr.Core.Providers;

namespace Issuarr.Core.Test.PipelineTests;

[TestFixture]
public class IndexerParsingFixture
{
    private const string Torznab = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:torznab="http://torznab.com/schemas/2015/feed">
          <channel>
            <item>
              <title>Iron Tide 046 (2026) (Digital) (Zone-Empire)</title>
              <guid>https://tracker/details/1</guid>
              <link>https://tracker/download/1.torrent</link>
              <pubDate>Tue, 24 Nov 2026 10:00:00 +0000</pubDate>
              <enclosure url="https://tracker/download/1.torrent" length="81000000" type="application/x-bittorrent" />
              <torznab:attr name="seeders" value="12" />
              <torznab:attr name="peers" value="15" />
              <torznab:attr name="infohash" value="ABCDEF" />
            </item>
            <item><title></title><link></link></item>
          </channel>
        </rss>
        """;

    [Test]
    public void should_parse_torznab_items()
    {
        var def = new ProviderDefinition { Id = 3, Name = "Tracker", Priority = 10 };
        var r = NewznabBase.Parse(XDocument.Parse(Torznab), def, DownloadProtocol.Torrent).Single();

        Assert.Multiple(() =>
        {
            Assert.That(r.Title, Is.EqualTo("Iron Tide 046 (2026) (Digital) (Zone-Empire)"));
            Assert.That(r.DownloadUrl, Is.EqualTo("https://tracker/download/1.torrent"));
            Assert.That(r.Size, Is.EqualTo(81000000));
            Assert.That(r.Seeders, Is.EqualTo(12));
            Assert.That(r.Peers, Is.EqualTo(15));
            Assert.That(r.Indexer, Is.EqualTo("Tracker"));
            Assert.That(r.IndexerPriority, Is.EqualTo(10));
            Assert.That(r.PublishDate, Is.EqualTo(new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc)));
        });
    }

    [Test]
    public async Task newznab_error_should_become_readable_message()
    {
        var http = new FakeHttp { Respond = (_, _) => FakeHttp.Text("<error code=\"100\" description=\"Incorrect user credentials\"/>") };
        var indexer = new NewznabIndexer(new FakeHttpFactory(http));
        var def = new ProviderDefinition { Name = "Geek", Settings = new() { ["baseUrl"] = "https://api.example", ["apiKey"] = "x" } };

        var e = Assert.ThrowsAsync<ProviderTestException>(() => indexer.Test(def, CancellationToken.None));
        Assert.That(e!.Message, Does.Contain("Invalid API key"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task newznab_search_should_send_query_categories_and_key()
    {
        var http = new FakeHttp { Respond = (_, _) => FakeHttp.Text("<rss><channel></channel></rss>") };
        var indexer = new NewznabIndexer(new FakeHttpFactory(http));
        var def = new ProviderDefinition { Name = "Geek", Settings = new() { ["baseUrl"] = "https://api.example/", ["apiKey"] = "KEY" } };

        await indexer.Search(def, "Circuit & Solder", CancellationToken.None);

        var url = http.Requests.Single().Url;
        Assert.That(url, Does.StartWith("https://api.example/api?"));
        Assert.That(url, Does.Contain("t=search").And.Contain("q=Circuit%20%26%20Solder").And.Contain("cat=7030%2C7010").And.Contain("apikey=KEY"));
    }

    [Test]
    public void should_parse_prowlarr_results()
    {
        const string json = """
            [
              {"guid":"g1","title":"Neon Alley 013 (2026)","downloadUrl":"http://p/1","size":5000,"indexer":"Nyaa","protocol":"torrent","seeders":4,"leechers":1,"publishDate":"2026-11-20T10:00:00Z"},
              {"guid":"g2","title":"Circuit and Solder November 2026","downloadUrl":"http://p/2","size":9000,"indexer":"Geek","protocol":"usenet"},
              {"guid":"g3","title":"No link","protocol":"usenet"}
            ]
            """;
        var list = ProwlarrIndexer.Parse(json, new ProviderDefinition { Id = 1, Name = "Prowlarr" });

        Assert.That(list, Has.Count.EqualTo(2));
        Assert.That(list[0].Protocol, Is.EqualTo(DownloadProtocol.Torrent));
        Assert.That(list[0].Seeders, Is.EqualTo(4));
        Assert.That(list[0].Indexer, Is.EqualTo("Nyaa (Prowlarr)"));
        Assert.That(list[1].Protocol, Is.EqualTo(DownloadProtocol.Usenet));
        Assert.That(list[1].Seeders, Is.Null);
    }

    [Test]
    public void should_compute_torrent_info_hash()
    {
        // d8:announce3:url4:infod4:name4:test6:lengthi5eee
        var info = "d4:name4:test6:lengthi5ee";
        var torrent = Encoding.ASCII.GetBytes($"d8:announce3:url4:info{info}e");
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.ASCII.GetBytes(info))).ToLowerInvariant();

        Assert.That(TorrentFetcher.InfoHash(torrent), Is.EqualTo(expected));
    }

    [TestCase("magnet:?xt=urn:btih:ABCDEF0123456789ABCDEF0123456789ABCDEF01&dn=x", "abcdef0123456789abcdef0123456789abcdef01")]
    [TestCase("magnet:?xt=urn:btih:VPJ6KOI4NNGLLU2EY4ZRXFLS2XZ3XLPB&dn=x", "abd3e5391c6b4cb5d344c7331b9572d5f3bbade1")]
    public void should_read_magnet_hash(string magnet, string hash)
    {
        Assert.That(TorrentFetcher.MagnetHash(magnet), Is.EqualTo(hash));
    }
}
