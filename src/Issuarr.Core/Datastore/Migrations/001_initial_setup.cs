using FluentMigrator;

namespace Issuarr.Core.Datastore.Migrations;

[Migration(1, "Initial setup")]
public class InitialSetup : Migration
{
    public override void Up()
    {
        Create.Table("Titles")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("SortName").AsString().NotNullable().Indexed()
            .WithColumn("Slug").AsString().NotNullable().Unique()
            .WithColumn("Kind").AsInt32().NotNullable()
            .WithColumn("Publisher").AsString().Nullable()
            .WithColumn("Year").AsInt32().NotNullable()
            .WithColumn("Frequency").AsString().Nullable()
            .WithColumn("Status").AsInt32().NotNullable()
            .WithColumn("Monitored").AsBoolean().NotNullable()
            .WithColumn("QualityProfile").AsString().NotNullable()
            .WithColumn("Path").AsString().NotNullable()
            .WithColumn("Overview").AsString().Nullable()
            .WithColumn("Tags").AsString().NotNullable().WithDefaultValue("[]")
            .WithColumn("Palette").AsString().NotNullable().WithDefaultValue("[]")
            .WithColumn("Added").AsString().NotNullable();

        Create.Table("Issues")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TitleId").AsInt32().NotNullable().Indexed()
                .ForeignKey("FK_Issues_Titles", "Titles", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("Number").AsString().NotNullable()
            .WithColumn("SortKey").AsDouble().NotNullable()
            .WithColumn("IssueTitle").AsString().Nullable()
            .WithColumn("ReleaseDate").AsString().NotNullable().Indexed()
            .WithColumn("Monitored").AsBoolean().NotNullable()
            .WithColumn("IssueFileId").AsInt32().Nullable();

        Create.Table("IssueFiles")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TitleId").AsInt32().NotNullable().Indexed()
                .ForeignKey("FK_IssueFiles_Titles", "Titles", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("RelativePath").AsString().NotNullable()
            .WithColumn("Size").AsInt64().NotNullable()
            .WithColumn("Format").AsString().NotNullable()
            .WithColumn("DateAdded").AsString().NotNullable();

        Create.Table("RootFolders")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Path").AsString().NotNullable().Unique();
    }

    public override void Down()
    {
        Delete.Table("IssueFiles");
        Delete.Table("Issues");
        Delete.Table("Titles");
        Delete.Table("RootFolders");
    }
}
