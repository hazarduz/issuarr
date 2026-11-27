using FluentMigrator;

namespace Issuarr.Core.Datastore.Migrations;

[Migration(2, "Settings, providers, history, blocklist, metadata ids")]
public class ProvidersAndHistory : Migration
{
    public override void Up()
    {
        Create.Table("Config")
            .WithColumn("Key").AsString().PrimaryKey()
            .WithColumn("Value").AsString().Nullable();

        foreach (var table in new[] { "Indexers", "DownloadClients", "Notifications" })
        {
            Create.Table(table)
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("Name").AsString().NotNullable()
                .WithColumn("Implementation").AsString().NotNullable()
                .WithColumn("Enable").AsBoolean().NotNullable()
                .WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(25)
                .WithColumn("Settings").AsString().NotNullable().WithDefaultValue("{}");
        }

        Create.Table("History")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TitleId").AsInt32().NotNullable().Indexed()
            .WithColumn("IssueId").AsInt32().Nullable()
            .WithColumn("EventType").AsInt32().NotNullable()
            .WithColumn("SourceTitle").AsString().NotNullable()
            .WithColumn("DownloadId").AsString().Nullable().Indexed()
            .WithColumn("Date").AsString().NotNullable().Indexed()
            .WithColumn("Data").AsString().NotNullable().WithDefaultValue("{}");

        Create.Table("Blocklist")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TitleId").AsInt32().NotNullable()
            .WithColumn("IssueId").AsInt32().Nullable()
            .WithColumn("SourceTitle").AsString().NotNullable()
            .WithColumn("Protocol").AsInt32().NotNullable()
            .WithColumn("Indexer").AsString().Nullable()
            .WithColumn("Message").AsString().Nullable()
            .WithColumn("Date").AsString().NotNullable();

        Alter.Table("Titles")
            .AddColumn("MetadataSource").AsString().Nullable()
            .AddColumn("ExternalId").AsString().Nullable()
            .AddColumn("CoverUrl").AsString().Nullable()
            .AddColumn("LastRefreshed").AsString().Nullable();

        Alter.Table("Issues")
            .AddColumn("ExternalId").AsString().Nullable()
            .AddColumn("CoverUrl").AsString().Nullable();
    }

    public override void Down()
    {
    }
}
