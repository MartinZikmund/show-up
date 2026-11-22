using AwesomeAssertions;
using ShowUp.Core.Storage;
using SQLite;

namespace ShowUp.Core.Tests.Storage;

[TestClass]
public class MigrationsTests : StorageTestBase
{
    [TestMethod]
    public async Task Migrations_FreshDb_SetsLatestUserVersion()
    {
        var connection = await Db.GetConnectionAsync();

        int version = await connection.ExecuteScalarAsync<int>("PRAGMA user_version");

        version.Should().Be(Migrations.LatestVersion).And.Be(6);
    }

    [TestMethod]
    public async Task Migrations_ReopenExistingDb_IsIdempotent()
    {
        var repo = CreateRepository();
        await repo.EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));
        await Db.DisposeAsync();

        var reopened = await Db.GetConnectionAsync();

        (await reopened.ExecuteScalarAsync<int>("PRAGMA user_version")).Should().Be(Migrations.LatestVersion);
        (await CreateRepository().GetActiveEnrollmentsAsync()).Should().ContainSingle();
    }

    [TestMethod]
    public async Task Migrate_V1ToV2_AddsInkColumnKeepsRows()
    {
        string path = Path.Combine(Path.GetTempPath(), $"showup-v1-{Guid.NewGuid():N}.db");
        SQLiteAsyncConnection connection = new(path);
        try
        {
            await Migrations.ApplyAsync(connection, targetVersion: 1);
            await connection.ExecuteAsync(
                """INSERT INTO "CompletionEntity" ("Id", "EnrollmentId", "DayIndex", "SessionIndex") VALUES ('c1', 'e1', 4, 1)""");

            await Migrations.ApplyAsync(connection);

            (await connection.ExecuteScalarAsync<int>("PRAGMA user_version")).Should().Be(Migrations.LatestVersion);
            var columns = await connection.QueryScalarsAsync<string>("""SELECT "name" FROM pragma_table_info('CompletionEntity')""");
            columns.Should().Contain("InkJson");
            (await connection.ExecuteScalarAsync<int>("""SELECT "DayIndex" FROM "CompletionEntity" WHERE "Id" = 'c1'""")).Should().Be(4);
            (await connection.ExecuteScalarAsync<int>("""SELECT COUNT(*) FROM "CompletionEntity" WHERE "InkJson" IS NULL""")).Should().Be(1);
        }
        finally
        {
            await connection.CloseAsync();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task Migrate_V2ToV3_KeepsData()
    {
        string path = Path.Combine(Path.GetTempPath(), $"showup-v2-{Guid.NewGuid():N}.db");
        SQLiteAsyncConnection connection = new(path);
        try
        {
            await Migrations.ApplyAsync(connection, targetVersion: 2);
            await connection.ExecuteAsync(
                """INSERT INTO "CompletionEntity" ("Id", "EnrollmentId", "DayIndex", "SessionIndex", "InkJson") VALUES ('c1', 'e1', 4, 1, '[]')""");
            await connection.ExecuteAsync("""INSERT INTO "EnrollmentEntity" ("Id", "ProgramId") VALUES ('e1', 'insanity')""");

            await Migrations.ApplyAsync(connection);

            (await connection.ExecuteScalarAsync<int>("PRAGMA user_version")).Should().Be(Migrations.LatestVersion);
            (await connection.ExecuteScalarAsync<string>("""SELECT "InkJson" FROM "CompletionEntity" WHERE "Id" = 'c1'""")).Should().Be("[]");
            (await connection.ExecuteScalarAsync<string>("""SELECT "ProgramId" FROM "EnrollmentEntity" WHERE "Id" = 'e1'""")).Should().Be("insanity");
            var noteColumns = await connection.QueryScalarsAsync<string>("""SELECT "name" FROM pragma_table_info('NoteEntity')""");
            noteColumns.Should().Contain(["Id", "UserId", "UpdatedAt", "DeletedAt", "DeviceId", "EnrollmentId", "DayIndex", "SessionIndex", "Text"]);
            var metricColumns = await connection.QueryScalarsAsync<string>("""SELECT "name" FROM pragma_table_info('MetricEntryEntity')""");
            metricColumns.Should().Contain(["EnrollmentId", "DayIndex", "SessionIndex", "BonusWorkoutId", "WorkoutId", "MetricId", "Value", "TextValue"]);
        }
        finally
        {
            await connection.CloseAsync();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task Migrate_Fresh_HasV4Tables()
    {
        var connection = await Db.GetConnectionAsync();

        (await connection.ExecuteScalarAsync<int>("PRAGMA user_version")).Should().Be(Migrations.LatestVersion);
        foreach (string table in new[] { "ReminderEntity", "CustomProgramEntity", "SyncedSettingEntity" })
        {
            (await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM pragma_table_info('{table}')")).Should().BeGreaterThan(0);
        }
    }

    [TestMethod]
    public async Task Migrate_V3Db_ToV4_KeepsData()
    {
        string path = Path.Combine(Path.GetTempPath(), $"showup-v3-{Guid.NewGuid():N}.db");
        SQLiteAsyncConnection connection = new(path);
        try
        {
            await Migrations.ApplyAsync(connection, targetVersion: 3);
            await connection.ExecuteAsync("""INSERT INTO "NoteEntity" ("Id", "SubjectKey", "Text") VALUES ('n1', 'p:e1', 'hello')""");
            await connection.ExecuteAsync("""INSERT INTO "EnrollmentEntity" ("Id", "ProgramId") VALUES ('e1', 'insanity')""");

            await Migrations.ApplyAsync(connection);

            (await connection.ExecuteScalarAsync<int>("PRAGMA user_version")).Should().Be(Migrations.LatestVersion);
            (await connection.ExecuteScalarAsync<string>("""SELECT "Text" FROM "NoteEntity" WHERE "Id" = 'n1'""")).Should().Be("hello");
            (await connection.ExecuteScalarAsync<string>("""SELECT "ProgramId" FROM "EnrollmentEntity" WHERE "Id" = 'e1'""")).Should().Be("insanity");
            var reminderColumns = await connection.QueryScalarsAsync<string>("""SELECT "name" FROM pragma_table_info('ReminderEntity')""");
            reminderColumns.Should().Contain(["Id", "UserId", "UpdatedAt", "DeletedAt", "DeviceId", "MinutesOfDay", "DaysMask", "Enabled"]);
            var programColumns = await connection.QueryScalarsAsync<string>("""SELECT "name" FROM pragma_table_info('CustomProgramEntity')""");
            programColumns.Should().Contain(["SubjectKey", "Revision", "ProgramJson"]);
            var settingColumns = await connection.QueryScalarsAsync<string>("""SELECT "name" FROM pragma_table_info('SyncedSettingEntity')""");
            settingColumns.Should().Contain(["SubjectKey", "Key", "Value"]);
        }
        finally
        {
            await connection.CloseAsync();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task Migrate_V4ToV5_MarksExistingRowsPendingAndAddsSyncState()
    {
        string path = Path.Combine(Path.GetTempPath(), $"showup-v4-{Guid.NewGuid():N}.db");
        SQLiteAsyncConnection connection = new(path);
        try
        {
            await Migrations.ApplyAsync(connection, targetVersion: 4);
            await connection.ExecuteAsync("""INSERT INTO "CompletionEntity" ("Id", "UserId", "DayIndex") VALUES ('c1', 'u1', 3)""");
            await connection.ExecuteAsync("""INSERT INTO "SyncedSettingEntity" ("Id", "SubjectKey", "Value") VALUES ('s1', 'k:x', '1')""");

            await Migrations.ApplyAsync(connection);

            (await connection.ExecuteScalarAsync<int>("PRAGMA user_version")).Should().Be(Migrations.LatestVersion);
            (await connection.ExecuteScalarAsync<int>("""SELECT "SyncPending" FROM "CompletionEntity" WHERE "Id" = 'c1'""")).Should().Be(1);
            (await connection.ExecuteScalarAsync<int>("""SELECT "SyncPending" FROM "SyncedSettingEntity" WHERE "Id" = 's1'""")).Should().Be(1);
            (await connection.ExecuteScalarAsync<int>("""SELECT "DayIndex" FROM "CompletionEntity" WHERE "Id" = 'c1'""")).Should().Be(3);
            foreach (string table in SqliteLocalDataClaim.SyncedTables)
            {
                var columns = await connection.QueryScalarsAsync<string>($"SELECT \"name\" FROM pragma_table_info('{table}')");
                columns.Should().Contain("SyncPending", table);
            }

            var stateColumns = await connection.QueryScalarsAsync<string>("""SELECT "name" FROM pragma_table_info('sync_state')""");
            stateColumns.Should().Equal("TableName", "UserId", "PullCursor", "PullLastId");
        }
        finally
        {
            await connection.CloseAsync();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task Migrate_V5ToV6_AddsCatalogCacheAndKeepsData()
    {
        string path = Path.Combine(Path.GetTempPath(), $"showup-v5-{Guid.NewGuid():N}.db");
        SQLiteAsyncConnection connection = new(path);
        try
        {
            await Migrations.ApplyAsync(connection, targetVersion: 5);
            await connection.ExecuteAsync("""INSERT INTO "CompletionEntity" ("Id", "DayIndex") VALUES ('c1', 3)""");

            await Migrations.ApplyAsync(connection);

            (await connection.ExecuteScalarAsync<int>("PRAGMA user_version")).Should().Be(6);
            (await connection.ExecuteScalarAsync<int>("""SELECT "DayIndex" FROM "CompletionEntity" WHERE "Id" = 'c1'""")).Should().Be(3);
            (await connection.QueryScalarsAsync<string>("""SELECT "name" FROM pragma_table_info('catalog_cache_state')""")).Should().Equal("Id", "Version", "WorkoutsJson");
            (await connection.QueryScalarsAsync<string>("""SELECT "name" FROM pragma_table_info('catalog_cache_program')""")).Should().Equal("ProgramId", "Revision", "Version", "Json");
        }
        finally
        {
            await connection.CloseAsync();
            File.Delete(path);
        }
    }
}
