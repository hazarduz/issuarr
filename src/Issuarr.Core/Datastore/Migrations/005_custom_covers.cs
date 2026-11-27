using FluentMigrator;

namespace Issuarr.Core.Datastore.Migrations;

[Migration(5, "Custom covers")]
public class CustomCovers : Migration
{
    public override void Up() => Alter.Table("Titles").AddColumn("CustomCover").AsString().Nullable();

    public override void Down()
    {
    }
}
