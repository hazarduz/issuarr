using FluentMigrator;

namespace Issuarr.Core.Datastore.Migrations;

[Migration(6, "KOReader sync")]
public class KoSync : Migration
{
    public override void Up()
    {
        Alter.Table("IssueFiles").AddColumn("PartialMd5").AsString().Nullable().Indexed();

        Create.Table("KoSyncProgress")
            .WithColumn("Document").AsString().PrimaryKey()
            .WithColumn("Progress").AsString().NotNullable()
            .WithColumn("Percentage").AsDouble().NotNullable()
            .WithColumn("Device").AsString().Nullable()
            .WithColumn("DeviceId").AsString().Nullable()
            .WithColumn("Timestamp").AsInt64().NotNullable()
            .WithColumn("IssueId").AsInt32().Nullable();
    }

    public override void Down()
    {
    }
}
