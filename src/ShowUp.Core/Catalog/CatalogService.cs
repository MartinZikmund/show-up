using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace ShowUp.Core.Catalog;

public sealed class CatalogService(IBundledCatalogSource source, ILogger<CatalogService> logger) : ICatalogService
{
    public const int BundledCatalogVersion = 1;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConditionalWeakTable<ProgramTrack, IReadOnlyList<ScheduleDay>> _days = new();
    private WorkoutCatalog? _catalog;
    private IReadOnlyDictionary<string, Workout> _workoutsById = new Dictionary<string, Workout>();

    public async Task<WorkoutCatalog> GetCatalogAsync()
    {
        if (_catalog is { } loaded)
        {
            return loaded;
        }

        await _gate.WaitAsync();
        try
        {
            return _catalog ??= await LoadAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkoutProgram?> GetProgramAsync(string programId, int? revision = null)
    {
        var catalog = await GetCatalogAsync();
        var matches = catalog.Programs.Where(p => p.Id == programId);
        return revision is { } r
            ? matches.FirstOrDefault(p => p.Revision == r)
            : matches.OrderByDescending(p => p.Revision).FirstOrDefault();
    }

    public async Task<Workout?> GetWorkoutAsync(string workoutId)
    {
        await GetCatalogAsync();
        return _workoutsById.GetValueOrDefault(workoutId);
    }

    public IReadOnlyList<ScheduleDay> GetTrackDays(ProgramTrack track) =>
        _days.GetValue(track, t => t.Days);

    private async Task<WorkoutCatalog> LoadAsync()
    {
        var workouts = CatalogParser.ParseWorkouts(await source.ReadWorkoutsAsync());
        List<WorkoutProgram> programs = new();

        foreach (var json in await source.ReadProgramsAsync())
        {
            if (!CatalogParser.TryParseProgram(json, out var program, out var error))
            {
                logger.LogWarning("Skipping unreadable catalog program: {Error}", error);
                continue;
            }

            var errors = CatalogValidator.Validate(new WorkoutCatalog(BundledCatalogVersion, workouts, [program]));
            if (errors.Count > 0)
            {
                logger.LogWarning("Skipping invalid catalog program '{ProgramId}': {Errors}", program.Id, string.Join(" ", errors));
                continue;
            }

            programs.Add(program);
        }

        WorkoutCatalog catalog = new(BundledCatalogVersion, workouts, programs);
        _workoutsById = catalog.WorkoutsById;
        return catalog;
    }
}
