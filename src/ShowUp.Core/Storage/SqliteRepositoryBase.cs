using System.Linq.Expressions;
using SQLite;

namespace ShowUp.Core.Storage;

/// <summary>User scoping, stamping and tombstones shared by the notes and metrics repositories.</summary>
public abstract class SqliteRepositoryBase(ShowUpDatabase db, TimeProvider time, IUserScope user, IDeviceIdProvider device)
{
    protected DateTimeOffset UtcNow => time.GetUtcNow();

    protected DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    protected bool IsVisible(SyncableEntity e) => e.DeletedAt is null && e.UserId == user.CurrentUserId;

    protected Task<SQLiteAsyncConnection> ConnectAsync() => db.GetConnectionAsync();

    protected T Stamp<T>(T entity)
        where T : SyncableEntity
    {
        DateTimeOffset now = time.GetUtcNow();
        entity.Id = Guid.NewGuid();
        entity.UserId = user.CurrentUserId;
        entity.DeviceId = device.DeviceId;
        entity.CreatedAt = now;
        entity.UpdatedAt = now;
        return entity;
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
        DateTimeOffset now = time.GetUtcNow();
        entity.UpdatedAt = now;
        entity.DeviceId = device.DeviceId;
        if (delete)
        {
            entity.DeletedAt = now;
        }

        connection.Update(entity);
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
