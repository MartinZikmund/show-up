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

        version.Should().Be(Migrations.LatestVersion).And.Be(3);
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

            (await connection.ExecuteScalarAsync<int>("PRAGMA user_version")).Should().Be(3);
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
}
