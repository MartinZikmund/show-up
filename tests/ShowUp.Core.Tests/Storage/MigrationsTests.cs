using AwesomeAssertions;

namespace ShowUp.Core.Tests.Storage;

[TestClass]
public class MigrationsTests : StorageTestBase
{
    [TestMethod]
    public async Task Migrations_FreshDb_SetsUserVersion1()
    {
        var connection = await Db.GetConnectionAsync();

        int version = await connection.ExecuteScalarAsync<int>("PRAGMA user_version");

        version.Should().Be(1);
    }

    [TestMethod]
    public async Task Migrations_ReopenExistingDb_IsIdempotent()
    {
        var repo = CreateRepository();
        await repo.EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));
        await Db.DisposeAsync();

        var reopened = await Db.GetConnectionAsync();

        (await reopened.ExecuteScalarAsync<int>("PRAGMA user_version")).Should().Be(1);
        (await CreateRepository().GetActiveEnrollmentsAsync()).Should().ContainSingle();
    }
}
