using System.Data;
using System.Text.Json;
using Dapper;

namespace Issuarr.Core.Datastore;

/// <summary>Stores simple string lists (tags, palette) as JSON text columns.</summary>
public class JsonListHandler : SqlMapper.TypeHandler<List<string>>
{
    public override void SetValue(IDbDataParameter parameter, List<string>? value) =>
        parameter.Value = JsonSerializer.Serialize(value ?? []);

    public override List<string> Parse(object value) =>
        value is string s && s.Length > 0 ? JsonSerializer.Deserialize<List<string>>(s) ?? [] : [];

    public static void Register() => SqlMapper.AddTypeHandler(new JsonListHandler());
}
