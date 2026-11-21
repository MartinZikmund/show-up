using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Tests.Storage;

public abstract class StorageTestBase
{
    private string _path = null!;

    internal ShowUpDatabase Db { get; private set; } = null!;
    internal FakeTimeProvider Time { get; private set; } = null!;
    internal FakeUserScope User { get; private set; } = null!;
    internal FakeDeviceIdProvider Device { get; private set; } = null!;

    [TestInitialize]
    public void BaseInit()
    {
        _path = Path.Combine(Path.GetTempPath(), $"showup-test-{Guid.NewGuid():N}.db");
        Db = new ShowUpDatabase(_path);
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero));
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        User = new FakeUserScope();
        Device = new FakeDeviceIdProvider();
    }

    [TestCleanup]
    public async Task BaseCleanup()
    {
        await Db.DisposeAsync();
        foreach (string file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    internal SqliteTrackingRepository CreateRepository() => new(Db, Time, User, Device);

    internal SqliteNotesRepository CreateNotesRepository() => new(Db, Time, User, Device);

    internal SqliteMetricsRepository CreateMetricsRepository() => new(Db, Time, User, Device);
}
