using System.Data;
using System.Text.Json;
using Dapper;

namespace Issuarr.Core.Datastore;

/// <summary>Stores provider settings and history data as JSON text.</summary>
public class JsonDictionaryHandler : SqlMapper.TypeHandler<Dictionary<string, string>>
{
    public override void SetValue(IDbDataParameter parameter, Dictionary<string, string>? value) =>
        parameter.Value = JsonSerializer.Serialize(value ?? new Dictionary<string, string>());

    public override Dictionary<string, string> Parse(object value) =>
        value is string s && s.Length > 0
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? new()
            : new();
}
