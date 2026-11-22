using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using ShowUp.Core.Storage;
using ShowUp.Core.Supabase;

namespace ShowUp.Core.Sync;

/// <summary>
/// The user tables of docs/supabase.md in memory, for tests and the debug build's simulated sign-in: row level security by
/// user, <c>keep_newest_row</c> (an older <c>updated_at</c> keeps the stored row but still bumps <c>server_updated_at</c>),
/// <c>server_updated_at</c> = the transaction's start time in whole microseconds, keyset paging by <c>(server_updated_at, id)</c>.
/// </summary>
public sealed class InMemorySyncServer(TimeProvider clock)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Dictionary<Guid, Stored>> _tables = [];

    /// <param name="transactionStart">A transaction that began then and commits now (late commit); null starts it now.</param>
    public ISupabaseRest ClientFor(Func<string?> userId, Func<DateTimeOffset>? transactionStart = null) =>
        new Client(this, userId, transactionStart);

    /// <summary>Every stored row of a table, any user, as the server holds it.</summary>
    public IReadOnlyList<JsonObject> Rows(string table)
    {
        lock (_gate)
        {
            return _tables.TryGetValue(table, out var rows) ? [.. rows.Values.Select(r => (JsonObject)r.Json.DeepClone())] : [];
        }
    }

    private IReadOnlyList<T> Upsert<T>(string table, string? userId, IReadOnlyList<T> rows, JsonTypeInfo<T> typeInfo, DateTimeOffset at)
    {
        string uid = userId ?? throw new SupabaseException(SupabaseErrorKind.NotSignedIn, "Sign in to sync.");
        List<JsonObject> incoming = [.. rows.Select(r => JsonSerializer.SerializeToNode(r, typeInfo)!.AsObject())];
        if (incoming.Select(r => string.Join(',', r.Select(p => p.Key).Order(StringComparer.Ordinal))).Distinct().Count() > 1)
        {
            throw new SupabaseException(SupabaseErrorKind.Validation, "All object keys must match.", 400, "PGRST102");
        }

        lock (_gate)
        {
            var stored = Table(table);
            // RLS fails the whole statement: nothing of the batch is written.
            foreach (var row in incoming)
            {
                Guid id = row["id"]!.GetValue<Guid>();
                if (row["user_id"]?.GetValue<string>() != uid || (stored.TryGetValue(id, out var existing) && existing.UserId != uid))
                {
                    throw new SupabaseException(SupabaseErrorKind.Forbidden, "new row violates row-level security policy", 403, "42501");
                }
            }

            string stamp = MicrosecondTimestampConverter.Format(at);
            List<T> result = [];
            foreach (var row in incoming)
            {
                Guid id = row["id"]!.GetValue<Guid>();
                DateTimeOffset updatedAt = UpdatedAt(row);
                if (stored.TryGetValue(id, out var existing) && updatedAt < existing.UpdatedAt)
                {
                    existing = existing with { ServerUpdatedAt = at };
                }
                else
                {
                    existing = new(row, uid, updatedAt, at);
                }

                existing.Json["server_updated_at"] = stamp;
                stored[id] = existing;
                result.Add(existing.Json.Deserialize(typeInfo)!);
            }

            return result;
        }
    }

    private IReadOnlyList<T> Select<T>(string table, string? userId, DateTimeOffset? cursor, Guid? lastId, int limit, JsonTypeInfo<T> typeInfo)
    {
        string uid = userId ?? throw new SupabaseException(SupabaseErrorKind.NotSignedIn, "Sign in to sync.");
        lock (_gate)
        {
            var from = cursor is { } c ? Timestamps.Truncate(c) : (DateTimeOffset?)null;
            string? after = lastId?.ToString("D");
            return
            [
                .. Table(table).Values
                    .Where(r => r.UserId == uid)
                    .Where(r => from is not { } f
                        || (after is null ? r.ServerUpdatedAt >= f : r.ServerUpdatedAt > f || (r.ServerUpdatedAt == f && string.CompareOrdinal(r.IdText, after) > 0)))
                    .OrderBy(r => r.ServerUpdatedAt)
                    .ThenBy(r => r.IdText, StringComparer.Ordinal)
                    .Take(limit)
                    .Select(r => r.Json.Deserialize(typeInfo)!),
            ];
        }
    }

    private Dictionary<Guid, Stored> Table(string table)
    {
        if (!_tables.TryGetValue(table, out var rows))
        {
            _tables[table] = rows = [];
        }

        return rows;
    }

    private static DateTimeOffset UpdatedAt(JsonObject row) =>
        Timestamps.Truncate(DateTimeOffset.Parse(row["updated_at"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture));

    private DateTimeOffset Now() => Timestamps.Truncate(clock.GetUtcNow());

    private sealed record Stored(JsonObject Json, string UserId, DateTimeOffset UpdatedAt, DateTimeOffset ServerUpdatedAt)
    {
        public string IdText => Json["id"]!.GetValue<string>();
    }

    private sealed class Client(InMemorySyncServer server, Func<string?> userId, Func<DateTimeOffset>? transactionStart) : ISupabaseRest
    {
        public Task<IReadOnlyList<T>> UpsertAsync<T>(string table, IReadOnlyList<T> rows, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var at = transactionStart is null ? server.Now() : Timestamps.Truncate(transactionStart());
            return Task.FromResult(rows.Count == 0 ? [] : server.Upsert(table, userId(), rows, typeInfo, at));
        }

        public Task<IReadOnlyList<T>> SelectPageAsync<T>(
            string table,
            DateTimeOffset? cursor,
            Guid? lastId,
            int limit,
            JsonTypeInfo<T> typeInfo,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(server.Select(table, userId(), cursor, lastId, limit, typeInfo));
        }
    }
}
