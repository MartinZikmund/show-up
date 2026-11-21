using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using ShowUp.Core.CustomPrograms;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Catalog;

/// <summary>The bundled catalog (read once) merged with the user's custom programs (re-read after <see cref="InvalidateCustomPrograms"/>).</summary>
public sealed class CatalogService(IBundledCatalogSource source, ICustomProgramRepository customPrograms, ILogger<CatalogService> logger) : ICatalogService
{
    public const int BundledCatalogVersion = 1;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConditionalWeakTable<ProgramTrack, IReadOnlyList<ScheduleDay>> _days = new();

    // Older or deleted custom revisions that enrollments pinned; their workouts join the merged catalog.
    private readonly ConcurrentDictionary<(string Id, int Revision), WorkoutProgram> _pinned = new();
    private WorkoutCatalog? _bundled;
    private volatile Merged? _merged;

    public async Task<WorkoutCatalog> GetCatalogAsync() => (await GetMergedAsync()).Catalog;

    public void InvalidateCustomPrograms() => _merged = null;

    public async Task<WorkoutProgram?> GetProgramAsync(string programId, int? revision = null)
    {
        var catalog = await GetCatalogAsync();
        var matches = catalog.Programs.Where(p => p.Id == programId);
        if (revision is not { } r)
        {
            return matches.OrderByDescending(p => p.Revision).FirstOrDefault();
        }

        if (matches.FirstOrDefault(p => p.Revision == r) is { } match)
        {
            return match;
        }

        return IsCustomId(programId) ? await ResolvePinnedAsync(programId, r) : null;
    }

    public async Task<Workout?> GetWorkoutAsync(string workoutId) =>
        (await GetMergedAsync()).WorkoutsById.GetValueOrDefault(workoutId);

    public IReadOnlyList<ScheduleDay> GetTrackDays(ProgramTrack track) =>
        _days.GetValue(track, t => t.Days);

    private static bool IsCustomId(string programId) => programId.StartsWith(SqliteCustomProgramRepository.IdPrefix, StringComparison.Ordinal);

    private async Task<Merged> GetMergedAsync()
    {
        if (_merged is { } merged)
        {
            return merged;
        }

        await _gate.WaitAsync();
        try
        {
            return _merged ??= await MergeAsync(_bundled ??= await LoadBundledAsync());
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WorkoutProgram?> ResolvePinnedAsync(string programId, int revision)
    {
        if (_pinned.TryGetValue((programId, revision), out var cached))
        {
            return cached;
        }

        if (await customPrograms.GetAsync(programId, revision) is not { } entity
            || ParseCustom(entity, (await GetBundledAsync()).Workouts) is not { } program)
        {
            return null;
        }

        if (_pinned.TryAdd((programId, revision), program))
        {
            InvalidateCustomPrograms();
        }

        return program;
    }

    private async Task<WorkoutCatalog> GetBundledAsync()
    {
        if (_bundled is { } bundled)
        {
            return bundled;
        }

        await _gate.WaitAsync();
        try
        {
            return _bundled ??= await LoadBundledAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Merged> MergeAsync(WorkoutCatalog bundled)
    {
        List<WorkoutProgram> custom = [];
        try
        {
            foreach (var entity in await customPrograms.GetAllAsync())
            {
                if (ParseCustom(entity, bundled.Workouts) is { } program)
                {
                    custom.Add(program);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reading custom programs failed; showing the bundled catalog only.");
        }

        // Latest revisions first, so a renamed custom workout shows its newest name.
        IEnumerable<Workout> customWorkouts = custom.Concat(_pinned.Values).SelectMany(p => p.Workouts ?? []);
        WorkoutCatalog catalog = new(bundled.Version, [.. bundled.Workouts, .. customWorkouts], [.. bundled.Programs, .. custom]);
        return new(catalog, catalog.WorkoutsById);
    }

    private WorkoutProgram? ParseCustom(CustomProgramEntity entity, IReadOnlyList<Workout> bundledWorkouts)
    {
        if (CustomProgramFactory.Parse(entity) is not { } program)
        {
            logger.LogWarning("Skipping unreadable custom program {SubjectKey} revision {Revision}.", entity.SubjectKey, entity.Revision);
            return null;
        }

        var errors = CatalogValidator.Validate(new WorkoutCatalog(BundledCatalogVersion, bundledWorkouts, [program]));
        if (errors.Count > 0)
        {
            logger.LogWarning("Skipping invalid custom program '{ProgramId}': {Errors}", program.Id, string.Join(" ", errors));
            return null;
        }

        return program;
    }

    private async Task<WorkoutCatalog> LoadBundledAsync()
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

            if (program.IsCustom || IsCustomId(program.Id))
            {
                logger.LogWarning("Skipping catalog program '{ProgramId}': custom programs only come from the builder.", program.Id);
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

        return new(BundledCatalogVersion, workouts, programs);
    }

    private sealed record Merged(WorkoutCatalog Catalog, IReadOnlyDictionary<string, Workout> WorkoutsById);
}
