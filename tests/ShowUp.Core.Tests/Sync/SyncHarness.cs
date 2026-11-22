using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Storage;
using ShowUp.Core.Supabase;
using ShowUp.Core.Sync;
using ShowUp.Core.Tests.Storage;

namespace ShowUp.Core.Tests.Sync;

/// <summary>One server and any number of devices, each with its own SQLite file and clock.</summary>
internal sealed class SyncWorld : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 12, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly List<SimDevice> _devices = [];

    public SyncWorld()
    {
        Server = new(ServerClock);
    }

    public FakeTimeProvider ServerClock { get; } = new(Start);

    public InMemorySyncServer Server { get; }

    public SimDevice AddDevice(string name, string? userId = "alice", TimeSpan skew = default)
    {
        SimDevice device = new(this, name, userId, skew);
        _devices.Add(device);
        return device;
    }

    /// <summary>Real time passes for everyone.</summary>
    public void Advance(TimeSpan by)
    {
        ServerClock.Advance(by);
        foreach (var device in _devices)
        {
            device.Clock.Advance(by);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var device in _devices)
        {
            await device.DisposeAsync();
        }
    }
}

internal sealed class SimDevice : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"showup-sync-{Guid.NewGuid():N}.db");

    public SimDevice(SyncWorld world, string name, string? userId, TimeSpan skew)
    {
        Clock = new(SyncWorld.Start + skew);
        Clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        User = new() { CurrentUserId = userId };
        DeviceId = new() { DeviceId = name };
        Db = new(_path);
        Rest = new(world.Server.ClientFor(() => User.CurrentUserId));
        Engine = new(Db, Rest, NullLogger<SyncEngine>.Instance);
    }

    public FakeTimeProvider Clock { get; }

    public FakeUserScope User { get; }

    public FakeDeviceIdProvider DeviceId { get; }

    public ShowUpDatabase Db { get; }

    public FaultyRest Rest { get; }

    public SyncEngine Engine { get; }

    public SqliteTrackingRepository Tracking => new(Db, Clock, User, DeviceId);

    public SqliteNotesRepository Notes => new(Db, Clock, User, DeviceId);

    public SqliteReminderRepository Reminders => new(Db, Clock, User, DeviceId);

    public SqliteSyncedSettings Settings => new(Db, Clock, User, DeviceId);

    public Task<SyncResult> SyncAsync() => Engine.SyncAsync(User.CurrentUserId!);

    public async Task<List<T>> AllRowsAsync<T>()
        where T : SyncableEntity, new()
    {
        var connection = await Db.GetConnectionAsync();
        return await connection.Table<T>().ToListAsync();
    }

    public async Task<long> CursorAsync(string table)
    {
        var connection = await Db.GetConnectionAsync();
        return await connection.ExecuteScalarAsync<long>("""SELECT "PullCursor" FROM "sync_state" WHERE "TableName" = ? AND "UserId" = ?""", table, User.CurrentUserId);
    }

    /// <summary>Many reminders in one go (faster than the repository's one insert each).</summary>
    public async Task AddRemindersAsync(int count)
    {
        var now = Timestamps.Truncate(Clock.GetUtcNow());
        var connection = await Db.GetConnectionAsync();
        await connection.InsertAllAsync(Enumerable.Range(0, count).Select(i => new ReminderEntity
        {
            Id = Guid.NewGuid(),
            UserId = User.CurrentUserId,
            DeviceId = DeviceId.DeviceId,
            CreatedAt = now,
            UpdatedAt = now,
            MinutesOfDay = i % 1440,
            DaysMask = 127,
            Enabled = true,
        }));
    }

    public async ValueTask DisposeAsync()
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
}

/// <summary>Passes calls through, counting them; <see cref="Fault"/> (operation, table, nth call of it for that table) can fail any one.</summary>
internal sealed class FaultyRest(ISupabaseRest inner) : ISupabaseRest
{
    private readonly Dictionary<(string, string), int> _calls = [];

    public Func<string, string, int, Exception?>? Fault { get; set; }

    public int Upserts { get; private set; }

    public int Selects { get; private set; }

    public List<int> UpsertSizes { get; } = [];

    public List<string> UpsertBodies { get; } = [];

    public Task<IReadOnlyList<T>> UpsertAsync<T>(string table, IReadOnlyList<T> rows, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        Upserts++;
        if (Fault?.Invoke("upsert", table, Count("upsert", table)) is { } error)
        {
            return Task.FromException<IReadOnlyList<T>>(error);
        }

        UpsertSizes.Add(rows.Count);
        UpsertBodies.AddRange(rows.Select(r => System.Text.Json.JsonSerializer.Serialize(r, typeInfo)));
        return inner.UpsertAsync(table, rows, typeInfo, ct);
    }

    public Task<IReadOnlyList<T>> SelectPageAsync<T>(string table, DateTimeOffset? cursor, Guid? lastId, int limit, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        Selects++;
        if (Fault?.Invoke("select", table, Count("select", table)) is { } error)
        {
            return Task.FromException<IReadOnlyList<T>>(error);
        }

        return inner.SelectPageAsync(table, cursor, lastId, limit, typeInfo, ct);
    }

    private int Count(string op, string table) => _calls[(op, table)] = _calls.GetValueOrDefault((op, table)) + 1;
}
