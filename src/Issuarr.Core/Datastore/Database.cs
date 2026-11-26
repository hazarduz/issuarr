using System.Data;
using Dapper;
using Issuarr.Core.Configuration;
using Microsoft.Data.Sqlite;

namespace Issuarr.Core.Datastore;

public interface IDatabase
{
    IDbConnection Open();
    string ConnectionString { get; }
}

public class Database : IDatabase
{
    static Database()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = false;
        SqlMapper.AddTypeHandler(new DateTimeUtcHandler());
        JsonListHandler.Register();
    }

    public Database(AppFolders folders)
        : this(new SqliteConnectionStringBuilder
        {
            DataSource = folders.DatabaseFile,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
        }.ToString())
    {
    }

    public Database(string connectionString)
    {
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public IDbConnection Open()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        return conn;
    }

    /// <summary>SQLite stores DateTimes as text; make sure they come back as UTC.</summary>
    private class DateTimeUtcHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override void SetValue(IDbDataParameter parameter, DateTime value) =>
            parameter.Value = value.ToUniversalTime().ToString("O");

        public override DateTime Parse(object value) =>
            DateTime.SpecifyKind(DateTime.Parse((string)value, null, System.Globalization.DateTimeStyles.RoundtripKind), DateTimeKind.Utc);
    }
}
