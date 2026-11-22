using System.Text.Json.Serialization.Metadata;

namespace ShowUp.Core.Supabase;

public interface ISupabaseRest
{
    /// <summary>
    /// <c>POST /rest/v1/{table}?on_conflict=id</c> with <c>resolution=merge-duplicates,return=representation</c>. Returns
    /// the stored rows: a stale update comes back as the winning row. Chunk big batches yourself.
    /// </summary>
    Task<IReadOnlyList<T>> UpsertAsync<T>(string table, IReadOnlyList<T> rows, JsonTypeInfo<T> typeInfo, CancellationToken ct = default);

    /// <summary>
    /// One page ordered by <c>server_updated_at, id</c>. With only <paramref name="cursor"/> it reads
    /// <c>server_updated_at &gt;= cursor</c> (the overlap); with <paramref name="lastId"/> too it resumes strictly after
    /// that <c>(server_updated_at, id)</c>. No <c>user_id</c> filter: RLS scopes the rows.
    /// </summary>
    Task<IReadOnlyList<T>> SelectPageAsync<T>(
        string table,
        DateTimeOffset? cursor,
        Guid? lastId,
        int limit,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct = default);

    /// <summary>
    /// One page of a public table (the catalog), read with the publishable key only: no session needed, works signed
    /// out. <paramref name="filter"/> is a raw PostgREST condition such as <c>version=lte.3</c>; <paramref name="order"/>
    /// must be a total order (e.g. <c>id.asc</c>) so offset paging is stable.
    /// </summary>
    Task<IReadOnlyList<T>> SelectAsync<T>(string table, string? filter, string order, int offset, int limit, JsonTypeInfo<T> typeInfo, CancellationToken ct = default);
}
