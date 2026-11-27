using FluentMigrator;

namespace Issuarr.Core.Datastore.Migrations;

[Migration(4, "Reading progress")]
public class ReadProgressTable : Migration
{
    public override void Up() =>
        Create.Table("ReadProgress")
            .WithColumn("IssueId").AsInt32().PrimaryKey()
                .ForeignKey("FK_ReadProgress_Issues", "Issues", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("Position").AsString().NotNullable()
            .WithColumn("Percent").AsDouble().NotNullable()
            .WithColumn("Updated").AsString().NotNullable();

    public override void Down() => Delete.Table("ReadProgress");
}
