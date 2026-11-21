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

        version.Should().Be(Migrations.LatestVersion).And.Be(2);
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

            (await connection.ExecuteScalarAsync<int>("PRAGMA user_version")).Should().Be(2);
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
}
