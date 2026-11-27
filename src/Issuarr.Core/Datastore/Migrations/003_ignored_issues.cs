using FluentMigrator;

namespace Issuarr.Core.Datastore.Migrations;

[Migration(3, "Hidden (ignored) issues")]
public class IgnoredIssues : Migration
{
    public override void Up() =>
        Alter.Table("Issues").AddColumn("Ignored").AsBoolean().NotNullable().WithDefaultValue(false);

    public override void Down()
    {
    }
}
