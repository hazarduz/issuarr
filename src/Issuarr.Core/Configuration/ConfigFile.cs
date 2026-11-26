using System.Security.Cryptography;
using System.Xml.Linq;

namespace Issuarr.Core.Configuration;

public enum AuthenticationMethod
{
    None,
    Basic,
    Forms,
    External,
}

/// <summary>
/// Reads and writes config.xml, the same flat format the other *arr apps use:
/// <code>&lt;Config&gt;&lt;Port&gt;8788&lt;/Port&gt;&lt;ApiKey&gt;...&lt;/ApiKey&gt;&lt;/Config&gt;</code>
/// Missing values are filled with defaults and written back on first run.
/// </summary>
public class ConfigFile
{
    private readonly string _path;
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public ConfigFile(AppFolders folders)
    {
        _path = folders.ConfigFile;
        Load();
        EnsureDefaults();
    }

    public int Port => int.TryParse(Get("Port"), out var p) ? p : 8788;
    public string BindAddress => Get("BindAddress") ?? "*";
    public string UrlBase => NormalizeUrlBase(Get("UrlBase"));
    public string ApiKey => Get("ApiKey")!;
    public string LogLevel => Get("LogLevel") ?? "info";
    public string InstanceName => Get("InstanceName") ?? "Issuarr";
    public AuthenticationMethod AuthenticationMethod =>
        Enum.TryParse<AuthenticationMethod>(Get("AuthenticationMethod"), true, out var m) ? m : AuthenticationMethod.None;

    public IReadOnlyDictionary<string, string> All
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public string? Get(string key)
    {
        lock (_lock)
        {
            return _values.TryGetValue(key, out var v) ? v : null;
        }
    }

    public void Set(string key, string value)
    {
        lock (_lock)
        {
            _values[key] = value;
            Save();
        }
    }

    public string RegenerateApiKey()
    {
        var key = NewApiKey();
        Set("ApiKey", key);
        return key;
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        var doc = XDocument.Load(_path);
        foreach (var el in doc.Root?.Elements() ?? [])
        {
            _values[el.Name.LocalName] = el.Value;
        }
    }

    private void EnsureDefaults()
    {
        var defaults = new Dictionary<string, Func<string>>
        {
            ["BindAddress"] = () => "*",
            ["Port"] = () => "8788",
            ["UrlBase"] = () => "",
            ["ApiKey"] = NewApiKey,
            ["AuthenticationMethod"] = () => nameof(AuthenticationMethod.None),
            ["LogLevel"] = () => "info",
            ["InstanceName"] = () => "Issuarr",
        };

        var changed = false;
        lock (_lock)
        {
            foreach (var (key, factory) in defaults)
            {
                if (!_values.TryGetValue(key, out var v) || (key == "ApiKey" && string.IsNullOrWhiteSpace(v)))
                {
                    _values[key] = factory();
                    changed = true;
                }
            }

            if (changed || !File.Exists(_path))
            {
                Save();
            }
        }
    }

    private void Save()
    {
        var doc = new XDocument(new XElement("Config", _values.Select(kv => new XElement(kv.Key, kv.Value))));
        var tmp = _path + ".tmp";
        doc.Save(tmp);
        File.Move(tmp, _path, overwrite: true);
    }

    private static string NewApiKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static string NormalizeUrlBase(string? urlBase)
    {
        if (string.IsNullOrWhiteSpace(urlBase))
        {
            return "";
        }

        return "/" + urlBase.Trim().Trim('/');
    }
}
