using FluentMigrator;

namespace Issuarr.Core.Datastore.Migrations;

[Migration(7, "Manually set release dates")]
public class LockedReleaseDates : Migration
{
    public override void Up() => Alter.Table("Issues").AddColumn("ReleaseDateLocked").AsBoolean().NotNullable().WithDefaultValue(false);

    public override void Down()
    {
    }
}
