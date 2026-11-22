using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using ShowUp.Core.Storage;
using ShowUp.Core.Supabase;
using SQLite;

namespace ShowUp.Core.Sync;

/// <summary>One synced table: how its rows go up (push) and come down (pull), see docs/sync.md.</summary>
public interface ISyncTable
{
    string LocalName { get; }

    string RemoteName { get; }

    /// <returns>Rows pushed, and rows the server answered with a newer winner that was applied here.</returns>
    Task<(int Pushed, int Applied)> PushAsync(SQLiteAsyncConnection connection, ISupabaseRest rest, SyncPass pass);

    /// <returns>Rows applied here.</returns>
    Task<int> PullAsync(SQLiteAsyncConnection connection, ISupabaseRest rest, SyncPass pass);
}

/// <summary>
/// One sync for one account. The session can switch accounts at any await: every request and every transaction checks
/// first, so nothing the next account's token fetched is written under this account (and its cursor never moves).
/// </summary>
public sealed class SyncPass(string userId, Func<bool>? isCurrent = null, CancellationToken ct = default)
{
    public string UserId => userId;

    public CancellationToken Token => ct;

    /// <exception cref="OperationCanceledException">Cancelled, or no longer the signed-in account.</exception>
    public void EnsureCurrent()
    {
        ct.ThrowIfCancellationRequested();
        if (isCurrent is not null && !isCurrent())
        {
            throw new OperationCanceledException("The account switched during the sync.");
        }
    }
}

public sealed class SyncTable<TEntity, TRow>(
    string remoteName,
    JsonTypeInfo<TRow> typeInfo,
    Action<TEntity, TRow> toRow,
    Action<TRow, TEntity> toEntity,
    ILogger logger) : ISyncTable
    where TEntity : SyncableEntity, new()
    where TRow : SyncRow, new()
{
    public string LocalName { get; } = typeof(TEntity).Name;

    public string RemoteName => remoteName;

    public async Task<(int Pushed, int Applied)> PushAsync(SQLiteAsyncConnection connection, ISupabaseRest rest, SyncPass pass)
    {
        string userId = pass.UserId;
        pass.EnsureCurrent();
        var pending = await connection.QueryAsync<TEntity>($"""SELECT * FROM "{LocalName}" WHERE "UserId" = ? AND "SyncPending" = 1""", userId);
        int pushed = 0;
        int applied = 0;
        foreach (var chunk in pending.Chunk(SyncEngine.PushBatchSize))
        {
            pass.EnsureCurrent();
            var stored = await rest.UpsertAsync(remoteName, [.. chunk.Select(ToRow)], typeInfo, pass.Token);
            var sent = chunk.ToDictionary(e => e.Id);
            int chunkApplied = 0;
            await connection.RunInTransactionAsync(c =>
            {
                pass.EnsureCurrent();
                foreach (var row in stored)
                {
                    if (row.UserId != userId || !sent.TryGetValue(row.Id, out var mine))
                    {
                        continue;
                    }

                    // Edited again while the push was out: it stays pending for the next one.
                    if (c.Find<TEntity>(row.Id) is not { } local || local.UpdatedAt != mine.UpdatedAt)
                    {
                        continue;
                    }

                    var serverAt = Timestamps.Truncate(row.UpdatedAt);
                    var mineAt = Timestamps.Truncate(mine.UpdatedAt);
                    if (serverAt == mineAt)
                    {
                        c.Execute($"""UPDATE "{LocalName}" SET "SyncPending" = 0 WHERE "Id" = ? AND "UpdatedAt" = ?""", row.Id.ToString("D"), mine.UpdatedAt.UtcTicks);
                    }
                    else if (serverAt > mineAt)
                    {
                        // The server kept a newer version (keep_newest_row): it wins here too.
                        c.InsertOrReplace(ToEntity(row));
                        chunkApplied++;
                    }
                    else
                    {
                        logger.LogWarning("The server answered {Table} {Id} with an older version; it stays pending.", remoteName, row.Id);
                    }
                }
            });
            applied += chunkApplied;
            pushed += chunk.Length;
        }

        return (pushed, applied);
    }

    public async Task<int> PullAsync(SQLiteAsyncConnection connection, ISupabaseRest rest, SyncPass pass)
    {
        string userId = pass.UserId;
        pass.EnsureCurrent();
        long ticks = await connection.ExecuteScalarAsync<long>(
            """SELECT "PullCursor" FROM "sync_state" WHERE "TableName" = ? AND "UserId" = ?""", LocalName, userId);
        DateTimeOffset? cursor = ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

        // A transaction that committed late can sit just below the cursor: re-read the last few seconds first.
        DateTimeOffset? from = cursor - SyncEngine.PullOverlap;
        Guid? after = null;
        int applied = 0;
        while (true)
        {
            pass.EnsureCurrent();
            var page = await rest.SelectPageAsync(remoteName, from, after, SyncEngine.PullPageSize, typeInfo, pass.Token);
            if (page.Count == 0)
            {
                break;
            }

            var last = page[^1];
            // Only this account's rows move its cursor.
            var ownLast = page.LastOrDefault(r => r.UserId == userId);
            int pageApplied = 0;
            DateTimeOffset? pageCursor = cursor;
            await connection.RunInTransactionAsync(c =>
            {
                pass.EnsureCurrent();
                foreach (var row in page)
                {
                    if (Apply(c, row, userId))
                    {
                        pageApplied++;
                    }
                }

                // Same transaction as the rows: a failure on a later page resumes from here.
                if (ownLast?.ServerUpdatedAt is { } at && (pageCursor is null || at > pageCursor))
                {
                    pageCursor = at;
                    c.Execute(
                        """INSERT OR REPLACE INTO "sync_state" ("TableName", "UserId", "PullCursor", "PullLastId") VALUES (?, ?, ?, ?)""",
                        LocalName,
                        userId,
                        at.UtcTicks,
                        ownLast.Id.ToString("D"));
                }
            });
            applied += pageApplied;
            cursor = pageCursor;

            if (page.Count < SyncEngine.PullPageSize || last.ServerUpdatedAt is not { } next)
            {
                break;
            }

            from = next;
            after = last.Id;
        }

        return applied;
    }

    // Last writer wins by UpdatedAt; a local edit not yet pushed only loses to a strictly newer version.
    private bool Apply(SQLiteConnection c, TRow row, string userId)
    {
        if (row.UserId != userId)
        {
            return false;
        }

        var local = c.Find<TEntity>(row.Id);
        if (local is not null && local.UserId != userId)
        {
            return false;
        }

        if (local is not null)
        {
            var remoteAt = Timestamps.Truncate(row.UpdatedAt);
            var localAt = Timestamps.Truncate(local.UpdatedAt);
            bool newer = remoteAt > localAt;
            bool sameVersionDifferentContent = remoteAt == localAt && !local.SyncPending && !SameContent(ToRow(local), row);
            if (!newer && !sameVersionDifferentContent)
            {
                return false;
            }
        }

        c.InsertOrReplace(ToEntity(row));
        return true;
    }

    private bool SameContent(TRow a, TRow b)
    {
        a.ServerUpdatedAt = null;
        var serverAt = b.ServerUpdatedAt;
        b.ServerUpdatedAt = null;
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(a, typeInfo).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(b, typeInfo));
        }
        finally
        {
            b.ServerUpdatedAt = serverAt;
        }
    }

    private TRow ToRow(TEntity entity)
    {
        TRow row = new()
        {
            Id = entity.Id,
            UserId = entity.UserId ?? "",
            DeviceId = entity.DeviceId,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            DeletedAt = entity.DeletedAt,
        };
        toRow(entity, row);
        return row;
    }

    private TEntity ToEntity(TRow row)
    {
        TEntity entity = new()
        {
            Id = row.Id,
            UserId = row.UserId,
            DeviceId = row.DeviceId,
            CreatedAt = Timestamps.Truncate(row.CreatedAt),
            UpdatedAt = Timestamps.Truncate(row.UpdatedAt),
            DeletedAt = row.DeletedAt is { } deleted ? Timestamps.Truncate(deleted) : null,
            SyncPending = false,
        };
        toEntity(row, entity);
        return entity;
    }
}
