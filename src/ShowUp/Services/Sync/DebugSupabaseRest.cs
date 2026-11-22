#if DEBUG
using System.Text.Json.Serialization.Metadata;
using ShowUp.Core.Supabase;
using ShowUp.Core.Sync;
using ShowUp.Services.Auth;

namespace ShowUp.Services.Sync;

/// <summary>
/// Debug builds only: with "Simulate sign-in" on, sync talks to an in-memory server (lost on restart) instead of Supabase,
/// so the sync status and "Sync now" can be tried without a hosted project.
/// </summary>
public sealed class DebugSupabaseRest(ISupabaseRest real, DebugSupabaseAuth auth, TimeProvider time) : ISupabaseRest
{
    private readonly ISupabaseRest _fake = new InMemorySyncServer(time).ClientFor(() => auth.Current?.UserId);

    private ISupabaseRest Target => auth.Simulate ? _fake : real;

    public Task<IReadOnlyList<T>> UpsertAsync<T>(string table, IReadOnlyList<T> rows, JsonTypeInfo<T> typeInfo, CancellationToken ct = default) =>
        Target.UpsertAsync(table, rows, typeInfo, ct);

    public Task<IReadOnlyList<T>> SelectPageAsync<T>(
        string table,
        DateTimeOffset? cursor,
        Guid? lastId,
        int limit,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct = default) =>
        Target.SelectPageAsync(table, cursor, lastId, limit, typeInfo, ct);

    public Task<IReadOnlyList<T>> SelectAsync<T>(string table, string? filter, string order, int offset, int limit, JsonTypeInfo<T> typeInfo, CancellationToken ct = default) =>
        real.SelectAsync(table, filter, order, offset, limit, typeInfo, ct);
}
#endif
