using System.Linq.Expressions;
using SQLite;

namespace ShowUp.Core.Storage;

/// <summary>
/// User scoping, stamping and tombstones shared by the repositories. Every write goes through <see cref="Insert{T}"/> or
/// <see cref="Touch"/>: whole-microsecond stamps, monotonic per row, the row marked for the next push.
/// </summary>
public abstract class SqliteRepositoryBase(ShowUpDatabase db, TimeProvider time, IUserScope user, IDeviceIdProvider device)
{
    protected DateTimeOffset UtcNow => Timestamps.Truncate(time.GetUtcNow());

    protected DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    protected bool IsVisible(SyncableEntity e) => e.DeletedAt is null && e.UserId == user.CurrentUserId;

    protected Task<SQLiteAsyncConnection> ConnectAsync() => db.GetConnectionAsync();

    /// <summary>Newest first: <c>UpdatedAt</c>, then <c>Id</c> (ordinal "D" text), so every device picks the same winner on a tie.</summary>
    protected static IOrderedEnumerable<T> NewestFirst<T>(IEnumerable<T> rows)
        where T : SyncableEntity => rows
        .OrderByDescending(r => r.UpdatedAt)
        .ThenByDescending(r => r.Id.ToString("D"), StringComparer.Ordinal);

    protected T Stamp<T>(T entity)
        where T : SyncableEntity
    {
        DateTimeOffset now = UtcNow;
        entity.Id = Guid.NewGuid();
        entity.UserId = user.CurrentUserId;
        entity.DeviceId = device.DeviceId;
        entity.CreatedAt = now;
        entity.UpdatedAt = now;
        entity.SyncPending = true;
        return entity;
    }

    protected async Task InsertAsync<T>(SQLiteAsyncConnection connection, T entity)
        where T : SyncableEntity
    {
        entity.SyncPending = true;
        await connection.InsertAsync(entity);
        db.NotifyLocalChange();
    }

    /// <summary><see cref="InsertAsync{T}"/> inside a transaction.</summary>
    protected void Insert<T>(SQLiteConnection connection, T entity)
        where T : SyncableEntity
    {
        entity.SyncPending = true;
        connection.Insert(entity);
        db.NotifyLocalChange();
    }

    /// <summary>Live rows of the current user only.</summary>
    protected Task<List<T>> QueryAsync<T>(SQLiteAsyncConnection connection, Expression<Func<T, bool>> where)
        where T : SyncableEntity, new()
    {
        string? uid = user.CurrentUserId;
        return connection.Table<T>().Where(where).Where(x => x.UserId == uid && x.DeletedAt == null).ToListAsync();
    }

    /// <summary><see cref="QueryAsync{T}"/> inside a transaction.</summary>
    protected List<T> Query<T>(SQLiteConnection connection, Expression<Func<T, bool>> where)
        where T : SyncableEntity, new()
    {
        string? uid = user.CurrentUserId;
        return connection.Table<T>().Where(where).Where(x => x.UserId == uid && x.DeletedAt == null).ToList();
    }

    protected void Touch(SQLiteConnection connection, SyncableEntity entity, bool delete = false)
    {
        DateTimeOffset stamp = Timestamps.Next(time.GetUtcNow(), entity.UpdatedAt);
        entity.UpdatedAt = stamp;
        entity.DeviceId = device.DeviceId;
        entity.SyncPending = true;
        if (delete)
        {
            entity.DeletedAt = stamp;
        }

        connection.Update(entity);
        db.NotifyLocalChange();
    }

    /// <summary>Tombstones rows that share a key with the kept one (two devices can write the same key before they sync).</summary>
    protected void Tombstone(SQLiteConnection connection, IEnumerable<SyncableEntity> rows)
    {
        foreach (var row in rows)
        {
            Touch(connection, row, delete: true);
        }
    }
}
