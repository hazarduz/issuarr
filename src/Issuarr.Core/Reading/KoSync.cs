using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Issuarr.Core.Configuration;
using Issuarr.Core.Datastore;
using Issuarr.Core.Issues;
using Issuarr.Core.Titles;
using Microsoft.Extensions.Logging;

namespace Issuarr.Core.Reading;

public class KoSyncRecord
{
    public string Document { get; set; } = "";

    /// <summary>Page number for PDF/comics, an XPointer for reflowable books (EPUB).</summary>
    public string Progress { get; set; } = "";

    public double Percentage { get; set; }
    public string? Device { get; set; }
    public string? DeviceId { get; set; }
    public long Timestamp { get; set; }
    public int? IssueId { get; set; }
}

public interface IKoSyncService
{
    bool Authorized(string? username, string? key);
    KoSyncRecord? Get(string document);
    long Save(KoSyncRecord record);
}

/// <summary>
/// Implements the KOReader progress sync ("kosync") server. Positions are stored exactly as
/// KOReader sends them (so KOReader devices sync with each other), and matched to issues/books
/// by KOReader's document fingerprint so Issuarr's own reading progress follows along.
/// </summary>
public class KoSyncService(
    IDatabase db,
    IConfigService config,
    IIssueFileRepository files,
    IIssueRepository issues,
    ITitleService titles,
    IReadProgressRepository progress,
    IClock clock,
    ILogger<KoSyncService> log) : IKoSyncService
{
    private static readonly HashSet<string> Paged = ["PDF", "CBZ", "CBR", "CB7"];

    public bool Authorized(string? username, string? key) =>
        config.KoSyncEnabled && config.KoSyncUsername.Length > 0 && config.KoSyncKey.Length > 0
        && string.Equals(username, config.KoSyncUsername, StringComparison.Ordinal)
        && key != null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key.ToLowerInvariant()), Encoding.UTF8.GetBytes(config.KoSyncKey));

    public KoSyncRecord? Get(string document)
    {
        using var conn = db.Open();
        var stored = conn.QuerySingleOrDefault<KoSyncRecord>("SELECT * FROM KoSyncProgress WHERE Document = @document", new { document });

        // If the book was read further in Issuarr's own reader since, offer that position
        // (page-based formats only: KOReader positions EPUBs by internal XPointers we can't produce).
        var match = stored?.IssueId is { } known ? (IssueId: known, Format: FormatOf(known)) : Find(document);
        if (match is { } m && Paged.Contains(m.Format) && progress.Get(m.IssueId) is { } web)
        {
            var webTime = DateTimeOffset.Parse(web.Updated, CultureInfo.InvariantCulture).ToUnixTimeSeconds();
            if ((stored == null || webTime > stored.Timestamp) && int.TryParse(web.Position, out var page0))
            {
                return new KoSyncRecord
                {
                    Document = document,
                    Progress = (page0 + 1).ToString(CultureInfo.InvariantCulture),
                    Percentage = Math.Round(web.Percent / 100, 4),
                    Device = "Issuarr",
                    DeviceId = "issuarr-web",
                    Timestamp = webTime,
                };
            }
        }

        return stored;
    }

    public long Save(KoSyncRecord record)
    {
        record.Timestamp = new DateTimeOffset(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var match = Find(record.Document);
        record.IssueId = match?.IssueId;

        using (var conn = db.Open())
        {
            conn.Execute(
                """
                INSERT INTO KoSyncProgress (Document, Progress, Percentage, Device, DeviceId, Timestamp, IssueId)
                VALUES (@Document, @Progress, @Percentage, @Device, @DeviceId, @Timestamp, @IssueId)
                ON CONFLICT(Document) DO UPDATE SET Progress = excluded.Progress, Percentage = excluded.Percentage,
                    Device = excluded.Device, DeviceId = excluded.DeviceId, Timestamp = excluded.Timestamp, IssueId = excluded.IssueId
                """,
                record);
        }

        if (match is { } m)
        {
            // Mirror into Issuarr's reading progress (Continue Reading, title page badges, web reader).
            var percent = Math.Clamp(record.Percentage * 100, 0, 100);
            var position = Paged.Contains(m.Format) && int.TryParse(record.Progress, out var page)
                ? Math.Max(0, page - 1).ToString(CultureInfo.InvariantCulture)
                : "pct:" + record.Percentage.ToString("0.#####", CultureInfo.InvariantCulture);
            progress.Save(new ReadProgress { IssueId = m.IssueId, Position = position, Percent = percent });
        }
        else
        {
            log.LogInformation("KOReader sync: no file in the library matches document {Document}", record.Document);
        }

        return record.Timestamp;
    }

    /// <summary>Finds the issue whose file has this fingerprint (binary method) or file-name MD5 (filename method).</summary>
    private (int IssueId, string Format)? Find(string document)
    {
        var doc = document.ToLowerInvariant();
        var all = files.All();
        var hit = all.FirstOrDefault(f => f.PartialMd5 == doc);

        if (hit == null)
        {
            // Fingerprint files that haven't been yet (cheap: twelve 1 KB reads per file).
            var titleMap = titles.All().ToDictionary(t => t.Id);
            foreach (var f in all.Where(f => f.PartialMd5 == null))
            {
                if (!titleMap.TryGetValue(f.TitleId, out var t))
                {
                    continue;
                }

                var path = Path.Combine(t.Path, f.RelativePath);
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    f.PartialMd5 = PartialMd5(path);
                    files.SetPartialMd5(f.Id, f.PartialMd5);
                }
                catch (IOException)
                {
                    continue;
                }

                if (f.PartialMd5 == doc)
                {
                    hit = f;
                    break;
                }
            }
        }

        hit ??= all.FirstOrDefault(f => FileNameMd5(Path.GetFileName(f.RelativePath)) == doc);
        if (hit == null)
        {
            return null;
        }

        var issue = issues.GetByTitle(hit.TitleId).FirstOrDefault(i => i.IssueFileId == hit.Id);
        return issue == null ? null : (issue.Id, hit.Format.ToUpperInvariant());
    }

    private string FormatOf(int issueId) =>
        issues.Get(issueId)?.IssueFileId is { } fid && files.Get(fid) is { } f ? f.Format.ToUpperInvariant() : "";

    /// <summary>
    /// KOReader's util.partialMD5: MD5 over 1 KB samples at offsets 0, 1 KB, 4 KB, 16 KB … 1 GB
    /// (1024 &lt;&lt; 2i for i = 0..10; LuaJIT's lshift by -2 for i = -1 wraps to offset 0).
    /// </summary>
    public static string PartialMd5(string path)
    {
        using var file = File.OpenRead(path);
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = new byte[1024];
        for (var i = -1; i <= 10; i++)
        {
            var offset = i < 0 ? 0L : 1024L << (2 * i);
            if (offset >= file.Length)
            {
                break;
            }

            file.Seek(offset, SeekOrigin.Begin);
            var read = 0;
            while (read < buffer.Length)
            {
                var n = file.Read(buffer, read, buffer.Length - read);
                if (n == 0)
                {
                    break;
                }

                read += n;
            }

            md5.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
    }

    public static string FileNameMd5(string fileName) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(fileName))).ToLowerInvariant();

    public static string KeyFor(string password) => FileNameMd5(password);
}
