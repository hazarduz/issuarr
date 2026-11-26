using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace Issuarr.Core.Datastore;

public static class DatabaseMigrator
{
    /// <summary>Brings the database schema up to date. Safe to call on every start.</summary>
    public static void Migrate(string connectionString)
    {
        using var services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSQLite()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(DatabaseMigrator).Assembly).For.Migrations())
            .BuildServiceProvider(false);

        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
