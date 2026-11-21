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
}
