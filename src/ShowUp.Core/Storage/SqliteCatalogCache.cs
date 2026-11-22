using ShowUp.Core.Catalog;

namespace ShowUp.Core.Storage;

/// <summary>The downloaded catalog in the app database, so WebAssembly persists it like everything else.</summary>
public sealed class SqliteCatalogCache(ShowUpDatabase db) : ICatalogCache
{
    public async Task<CachedCatalog?> LoadAsync()
    {
        var connection = await db.GetConnectionAsync();
        var state = (await connection.QueryAsync<StateRow>("""SELECT "Version", "WorkoutsJson" FROM "catalog_cache_state" WHERE "Id" = 1""")).FirstOrDefault();
        if (state is null)
        {
            return null;
        }

        var programs = await connection.QueryAsync<ProgramRow>("""SELECT "ProgramId", "Revision", "Version", "Json" FROM "catalog_cache_program" ORDER BY "ProgramId", "Revision" """);
        return new(state.Version, state.WorkoutsJson, [.. programs.Select(p => new CachedProgram(p.ProgramId, p.Revision, p.Version, p.Json))]);
    }

    public async Task SaveAsync(int version, string? workoutsJson, IReadOnlyList<CachedProgram> programs)
    {
        var connection = await db.GetConnectionAsync();
        await connection.RunInTransactionAsync(c =>
        {
            c.Execute("""INSERT OR REPLACE INTO "catalog_cache_state" ("Id", "Version", "WorkoutsJson") VALUES (1, ?, ?)""", version, workoutsJson);
            foreach (var program in programs)
            {
                c.Execute(
                    """INSERT OR REPLACE INTO "catalog_cache_program" ("ProgramId", "Revision", "Version", "Json") VALUES (?, ?, ?, ?)""",
                    program.Id,
                    program.Revision,
                    program.Version,
                    program.Json);
            }
        });
    }

    private sealed class StateRow
    {
        public int Version { get; set; }

        public string? WorkoutsJson { get; set; }
    }

    private sealed class ProgramRow
    {
        public string ProgramId { get; set; } = "";

        public int Revision { get; set; }

        public int Version { get; set; }

        public string Json { get; set; } = "";
    }
}
