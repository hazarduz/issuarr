using System.Text.Json.Serialization;
using Issuarr.Core.Reading;
using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.KoSync;

public record KoProgressRequest(
    [property: JsonPropertyName("document")] string Document,
    [property: JsonPropertyName("progress")] string? Progress,
    [property: JsonPropertyName("percentage")] double Percentage,
    [property: JsonPropertyName("device")] string? Device,
    [property: JsonPropertyName("device_id")] string? DeviceId);

/// <summary>
/// KOReader progress sync server (the koreader-sync-server protocol). In KOReader:
/// Tools → Progress sync → Custom sync server → http://&lt;issuarr&gt;/kosync, then Login.
/// Authentication uses the x-auth-user / x-auth-key headers (key = MD5 of the password).
/// </summary>
[ApiController]
[Route("kosync")]
public class KoSyncController(IKoSyncService sync) : ControllerBase
{
    [HttpGet("healthcheck")]
    public IActionResult Health() => Ok(new { state = "OK" });

    /// <summary>KOReader's "Register". Accounts are set up in Issuarr, so this only confirms the configured one.</summary>
    [HttpPost("users/create")]
    public IActionResult Create([FromBody] Dictionary<string, string> body)
    {
        if (sync.Authenticate(body.GetValueOrDefault("username"), body.GetValueOrDefault("password")) != null)
        {
            return StatusCode(StatusCodes.Status201Created, new { username = body["username"] });
        }

        return StatusCode(StatusCodes.Status402PaymentRequired, new
        {
            code = 2005,
            message = "Set a KOReader sync password in Issuarr (your account menu → My Account), then use Login with your Issuarr username.",
        });
    }

    [HttpGet("users/auth")]
    public IActionResult Auth() => UserId() != null ? Ok(new { authorized = "OK" }) : Unauthorized401();

    [HttpPut("syncs/progress")]
    public IActionResult Put([FromBody] KoProgressRequest req)
    {
        if (UserId() is not { } userId)
        {
            return Unauthorized401();
        }

        if (string.IsNullOrWhiteSpace(req.Document) || req.Progress == null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { code = 2003, message = "Invalid request" });
        }

        var timestamp = sync.Save(new KoSyncRecord
        {
            Document = req.Document.Trim().ToLowerInvariant(),
            Progress = req.Progress,
            Percentage = req.Percentage,
            Device = req.Device,
            DeviceId = req.DeviceId,
            UserId = userId,
        });
        return Ok(new { document = req.Document, timestamp });
    }

    [HttpGet("syncs/progress/{document}")]
    public IActionResult Get(string document)
    {
        if (UserId() is not { } userId)
        {
            return Unauthorized401();
        }

        var r = sync.Get(userId, document.Trim().ToLowerInvariant());
        if (r == null)
        {
            return Ok(new { });
        }

        return Ok(new Dictionary<string, object?>
        {
            ["document"] = document,
            ["progress"] = r.Progress,
            ["percentage"] = r.Percentage,
            ["device"] = r.Device,
            ["device_id"] = r.DeviceId,
            ["timestamp"] = r.Timestamp,
        });
    }

    private int? UserId() =>
        sync.Authenticate(Request.Headers["x-auth-user"].FirstOrDefault(), Request.Headers["x-auth-key"].FirstOrDefault());

    private ObjectResult Unauthorized401() => StatusCode(StatusCodes.Status401Unauthorized, new { code = 2001, message = "Unauthorized" });
}
