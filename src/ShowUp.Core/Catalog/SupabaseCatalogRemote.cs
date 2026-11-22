using ShowUp.Core.Supabase;

namespace ShowUp.Core.Catalog;

/// <summary>Reads the public catalog tables with the publishable key; works signed out.</summary>
public sealed class SupabaseCatalogRemote(ISupabaseRest rest, SupabaseOptions options) : ICatalogRemote
{
    public const int PageSize = 200;

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
        for (int offset = 0; ; offset += PageSize)
        {
            var page = await rest.SelectAsync("catalog_workouts", filter, "id.asc", offset, PageSize, CatalogRowsJsonContext.Default.CatalogWorkoutRow, ct);
            workouts.AddRange(page.Select(r => r.Json.GetRawText()));
            if (page.Count < PageSize)
            {
                break;
            }
        }

        List<RemoteProgram> programs = [];
        for (int offset = 0; ; offset += PageSize)
        {
            var page = await rest.SelectAsync("catalog_programs", filter, "id.asc,revision.asc", offset, PageSize, CatalogRowsJsonContext.Default.CatalogProgramRow, ct);
            programs.AddRange(page.Select(r => new RemoteProgram(r.Id, r.Revision, r.Json.GetRawText())));
            if (page.Count < PageSize)
            {
                break;
            }
        }

        return new(version, workouts, programs);
    }
}
