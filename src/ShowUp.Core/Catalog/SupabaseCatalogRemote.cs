using ShowUp.Core.Supabase;

namespace ShowUp.Core.Catalog;

/// <summary>Reads the public catalog tables with the publishable key; works signed out.</summary>
public sealed class SupabaseCatalogRemote(ISupabaseRest rest, SupabaseOptions options) : ICatalogRemote
{
    public const int PageSize = 200;

    /// <summary>A table longer than this is treated as a broken or hostile response, not downloaded.</summary>
    public const int MaxPages = 50;

    public bool IsAvailable => options.IsConfigured;

    public async Task<int?> GetLatestVersionAsync(CancellationToken ct = default)
    {
        var rows = await rest.SelectAsync("catalog_versions", null, "version.desc", 0, 1, CatalogRowsJsonContext.Default.CatalogVersionRow, ct);
        return rows.Count == 0 ? null : rows[0].Version;
    }

    public async Task<RemoteCatalog> DownloadAsync(int version, CancellationToken ct = default)
    {
        string filter = $"version=lte.{version}";
        List<string> workouts = [];
        for (int page = 0; ; page++)
        {
            var rows = await rest.SelectAsync("catalog_workouts", filter, "id.asc", page * PageSize, PageSize, CatalogRowsJsonContext.Default.CatalogWorkoutRow, ct);
            workouts.AddRange(rows.Select(r => r.Json.GetRawText()));
            if (rows.Count < PageSize)
            {
                break;
            }

            EnsureWithinCeiling(page);
        }

        List<RemoteProgram> programs = [];
        for (int page = 0; ; page++)
        {
            var rows = await rest.SelectAsync("catalog_programs", filter, "id.asc,revision.asc", page * PageSize, PageSize, CatalogRowsJsonContext.Default.CatalogProgramRow, ct);
            programs.AddRange(rows.Select(r => new RemoteProgram(r.Id, r.Revision, r.Json.GetRawText())));
            if (rows.Count < PageSize)
            {
                break;
            }

            EnsureWithinCeiling(page);
        }

        return new(version, workouts, programs);
    }

    private static void EnsureWithinCeiling(int page)
    {
        if (page + 1 >= MaxPages)
        {
            throw new InvalidOperationException($"The catalog is longer than {MaxPages * PageSize} rows.");
        }
    }
}
