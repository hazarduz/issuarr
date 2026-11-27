using Issuarr.Core.Configuration;
using Issuarr.Core.Organizer;
using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.Api.V1;

public record HostConfigResource
{
    public string BindAddress { get; init; } = "*";
    public int Port { get; init; }
    public string UrlBase { get; init; } = "";
    public string InstanceName { get; init; } = "Issuarr";
    public string ApiKey { get; init; } = "";
    public string AuthenticationMethod { get; init; } = "none";
    public string AuthenticationRequired { get; init; } = "enabled";
    public string Username { get; init; } = "";

    /// <summary>Write-only. Empty keeps the current password.</summary>
    public string? Password { get; init; }

    public bool HasPassword { get; init; }
}

[ApiController]
[Route("api/v1/config")]
public class ConfigController(IConfigService config, ConfigFile file) : ControllerBase
{
    private const string Hidden = "********";
    private static readonly string[] Secrets = ["comicVineApiKey", "metronPassword", "googleBooksApiKey", "kosyncKey"];

    /// <summary>Media management, metadata and download handling settings.</summary>
    [HttpGet]
    public Dictionary<string, string> Get() =>
        config.All().ToDictionary(kv => kv.Key, kv => Secrets.Contains(kv.Key) && kv.Value.Length > 0 ? Hidden : kv.Value);

    [HttpPut]
    public ActionResult<Dictionary<string, string>> Put([FromBody] Dictionary<string, string> values)
    {
        foreach (var key in Secrets.Where(k => values.GetValueOrDefault(k) == Hidden))
        {
            values.Remove(key);
        }

        // KOReader authenticates with the MD5 of the password, so that's what's stored; the key
        // itself can only be set by choosing a password.
        values.Remove("kosyncKey");
        if (values.Remove("kosyncPassword", out var syncPassword) && !string.IsNullOrEmpty(syncPassword))
        {
            values["kosyncKey"] = Issuarr.Core.Reading.KoSyncService.KeyFor(syncPassword);
        }

        foreach (var key in new[] { "comicIssueFormat", "magazineIssueFormat", "titleFolderFormat", "bookFileFormat", "authorFolderFormat" })
        {
            if (values.TryGetValue(key, out var fmt) && string.IsNullOrWhiteSpace(fmt))
            {
                return ValidationProblem($"{key} can't be empty");
            }
        }

        config.SaveMany(values);
        return Get();
    }

    [HttpGet("naming/tokens")]
    public string[] Tokens() => FileNameBuilder.Tokens;

    [HttpGet("host")]
    public HostConfigResource GetHost() => new()
    {
        BindAddress = file.BindAddress,
        Port = file.Port,
        UrlBase = file.UrlBase,
        InstanceName = file.InstanceName,
        ApiKey = file.ApiKey,
        AuthenticationMethod = file.AuthenticationMethod.ToString().ToLowerInvariant(),
        AuthenticationRequired = file.AuthenticationRequired == "DisabledForLocalAddresses" ? "disabledForLocalAddresses" : "enabled",
        Username = file.Username,
        HasPassword = file.PasswordHash.Length > 0,
    };

    [HttpPut("host")]
    public ActionResult<object> PutHost([FromBody] HostConfigResource r)
    {
        if (!Enum.TryParse<AuthenticationMethod>(r.AuthenticationMethod, true, out var method))
        {
            return ValidationProblem("Unknown authentication method");
        }

        if (r.Port is < 1 or > 65535)
        {
            return ValidationProblem("Port must be between 1 and 65535");
        }

        var settingPassword = !string.IsNullOrEmpty(r.Password);
        if (method is AuthenticationMethod.Forms or AuthenticationMethod.Basic)
        {
            if (string.IsNullOrWhiteSpace(r.Username))
            {
                return ValidationProblem("Choose a username to turn on login");
            }

            if (!settingPassword && file.PasswordHash.Length == 0)
            {
                return ValidationProblem("Choose a password to turn on login");
            }

            if (settingPassword && r.Password!.Length < 6)
            {
                return ValidationProblem("Password must be at least 6 characters");
            }
        }

        var restartNeeded = r.Port != file.Port || r.BindAddress != file.BindAddress || NormalizeBase(r.UrlBase) != file.UrlBase;
        file.SetMany(new Dictionary<string, string>
        {
            ["BindAddress"] = string.IsNullOrWhiteSpace(r.BindAddress) ? "*" : r.BindAddress.Trim(),
            ["Port"] = r.Port.ToString(),
            ["UrlBase"] = NormalizeBase(r.UrlBase),
            ["InstanceName"] = string.IsNullOrWhiteSpace(r.InstanceName) ? "Issuarr" : r.InstanceName.Trim(),
            ["AuthenticationMethod"] = method.ToString(),
            ["AuthenticationRequired"] = r.AuthenticationRequired == "disabledForLocalAddresses" ? "DisabledForLocalAddresses" : "Enabled",
            ["Username"] = r.Username.Trim(),
        });
        if (settingPassword)
        {
            file.SetPassword(r.Password!);
        }

        return new { config = GetHost(), restartNeeded };
    }

    [HttpPost("host/apikey")]
    public object RegenerateApiKey() => new { apiKey = file.RegenerateApiKey() };

    private static string NormalizeBase(string? b) => string.IsNullOrWhiteSpace(b) ? "" : "/" + b.Trim().Trim('/');
}
