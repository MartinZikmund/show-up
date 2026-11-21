using System.Linq.Expressions;
using SQLite;

namespace ShowUp.Core.Storage;

/// <summary>User scoping, stamping and tombstones shared by the notes and metrics repositories.</summary>
public abstract class SqliteRepositoryBase(ShowUpDatabase db, TimeProvider time, IUserScope user, IDeviceIdProvider device)
{
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

    protected async Task TouchAsync(SQLiteAsyncConnection connection, SyncableEntity entity, bool delete = false)
    {
        DateTimeOffset now = time.GetUtcNow();
        entity.UpdatedAt = now;
        entity.DeviceId = device.DeviceId;
        if (delete)
        {
            entity.DeletedAt = now;
        }

        await connection.UpdateAsync(entity);
    }

    /// <summary>Keeps the newest row and tombstones the rest (two devices can write the same key before they sync).</summary>
    protected async Task TombstoneDuplicatesAsync(SQLiteAsyncConnection connection, IEnumerable<SyncableEntity> duplicates)
    {
        foreach (var duplicate in duplicates)
        {
            await TouchAsync(connection, duplicate, delete: true);
        }
    }
}
