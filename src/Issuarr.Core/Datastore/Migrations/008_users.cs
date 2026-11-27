using FluentMigrator;

namespace Issuarr.Core.Datastore.Migrations;

/// <summary>
/// User accounts and invites. Reading progress and KOReader sync positions become per user;
/// everything saved so far belongs to the owner (user 1, whose login lives in config.xml).
/// </summary>
[Migration(8, "Users")]
public class Users : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL COLLATE NOCASE UNIQUE,
                PasswordHash TEXT NOT NULL,
                Role TEXT NOT NULL,
                KoSyncKey TEXT NULL,
                Created TEXT NOT NULL,
                LastSeen TEXT NULL);
            INSERT INTO Users (Id, Username, PasswordHash, Role, Created) VALUES (1, 'owner', '', 'admin', strftime('%Y-%m-%dT%H:%M:%SZ', 'now'));

            CREATE TABLE Invites (
                Token TEXT PRIMARY KEY,
                Role TEXT NOT NULL,
                Note TEXT NULL,
                Created TEXT NOT NULL,
                Expires TEXT NOT NULL);

            CREATE TABLE ReadProgress_new (
                UserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
                IssueId INTEGER NOT NULL REFERENCES Issues(Id) ON DELETE CASCADE,
                Position TEXT NOT NULL,
                Percent REAL NOT NULL,
                Updated TEXT NOT NULL,
                PRIMARY KEY (UserId, IssueId));
            INSERT INTO ReadProgress_new (UserId, IssueId, Position, Percent, Updated) SELECT 1, IssueId, Position, Percent, Updated FROM ReadProgress;
            DROP TABLE ReadProgress;
            ALTER TABLE ReadProgress_new RENAME TO ReadProgress;

            CREATE TABLE KoSyncProgress_new (
                UserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
                Document TEXT NOT NULL,
                Progress TEXT NOT NULL,
                Percentage REAL NOT NULL,
                Device TEXT NULL,
                DeviceId TEXT NULL,
                Timestamp INTEGER NOT NULL,
                IssueId INTEGER NULL,
                PRIMARY KEY (UserId, Document));
            INSERT INTO KoSyncProgress_new (UserId, Document, Progress, Percentage, Device, DeviceId, Timestamp, IssueId)
                SELECT 1, Document, Progress, Percentage, Device, DeviceId, Timestamp, IssueId FROM KoSyncProgress;
            DROP TABLE KoSyncProgress;
            ALTER TABLE KoSyncProgress_new RENAME TO KoSyncProgress;
            """);
    }

    public override void Down()
    {
    }
}
