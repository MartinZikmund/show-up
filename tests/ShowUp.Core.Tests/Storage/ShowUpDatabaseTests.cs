using AwesomeAssertions;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Tests.Storage;

[TestClass]
public class ShowUpDatabaseTests
{
    [TestMethod]
    public async Task GetConnection_PrepareStorageProvided_RunsOnceBeforeOpening()
    {
        string path = Path.Combine(Path.GetTempPath(), $"showup-test-{Guid.NewGuid():N}.db");
        int calls = 0;
        bool fileExistedDuringPrepare = true;
        ShowUpDatabase db = new(path, () =>
        {
            calls++;
            fileExistedDuringPrepare = File.Exists(path);
            return Task.CompletedTask;
        });

        try
        {
            await db.GetConnectionAsync();
            await db.GetConnectionAsync();

            calls.Should().Be(1);
            fileExistedDuringPrepare.Should().BeFalse();
        }
        finally
        {
            await db.DisposeAsync();
            foreach (string file in new[] { path, path + "-wal", path + "-shm" })
            {
                File.Delete(file);
            }
        }
    }
}
