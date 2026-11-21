using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ShowUp.Core.CustomPrograms;
using ShowUp.Core.Messages;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Catalog;

/// <summary>The bundled catalog (read once) merged with the user's custom programs (re-read after <see cref="InvalidateCustomPrograms"/>).</summary>
public sealed class CatalogService : ICatalogService
{
    public const int BundledCatalogVersion = 1;

    public const string UnknownWorkoutName = "Unknown workout";

    private readonly IBundledCatalogSource _source;
    private readonly ICustomProgramRepository _customPrograms;
    private readonly ILogger<CatalogService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConditionalWeakTable<ProgramTrack, IReadOnlyList<ScheduleDay>> _days = new();

    // Older or deleted custom revisions that enrollments pinned; their workouts join the merged catalog.
    private readonly ConcurrentDictionary<(string Id, int Revision), WorkoutProgram> _pinned = new();
    private WorkoutCatalog? _bundled;
    private volatile Merged? _merged;

    // Bumped by every invalidation, so a merge that raced one isn't cached.
    private int _generation;

    // Bumped by full invalidations only, so a bundled read that raced one isn't cached.
    private int _bundledGeneration;

    public CatalogService(IBundledCatalogSource source, ICustomProgramRepository customPrograms, ILogger<CatalogService> logger, IMessenger? messenger = null)
    {
        _source = source;
        _customPrograms = customPrograms;
        _logger = logger;
        messenger?.Register<CatalogService, DataChangedMessage>(this, static (r, _) => r.Invalidate());
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _bundledGeneration);
        Volatile.Write(ref _bundled, null);
        InvalidateCustomPrograms();
    }

    public async Task<WorkoutCatalog> GetCatalogAsync() => (await GetMergedAsync()).Catalog;

    public void InvalidateCustomPrograms()
    {
        Interlocked.Increment(ref _generation);
        _merged = null;
    }

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

    private static bool IsCustomId(string programId) => CustomProgramFactory.IsCustomId(programId);

    private async Task<Merged> GetMergedAsync()
    {
        if (_merged is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync();
        try
        {
            if (_merged is { } current)
            {
                return current;
            }

            int generation = Volatile.Read(ref _generation);
            var bundled = await GetBundledLockedAsync();
            var merged = await MergeAsync(bundled);
            if (generation == Volatile.Read(ref _generation))
            {
                _merged = merged;
            }

            return merged;
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

        if (await _customPrograms.GetAsync(programId, revision) is not { } entity
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
            return await GetBundledLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    // Call under the gate.
    private async Task<WorkoutCatalog> GetBundledLockedAsync()
    {
        if (Volatile.Read(ref _bundled) is { } cached)
        {
            return cached;
        }

        var generation = Volatile.Read(ref _bundledGeneration);
        var loaded = await LoadBundledAsync();
        if (generation == Volatile.Read(ref _bundledGeneration))
        {
            Volatile.Write(ref _bundled, loaded);
        }

        return loaded;
    }

    private async Task<Merged> MergeAsync(WorkoutCatalog bundled)
    {
        List<WorkoutProgram> custom = [];
        try
        {
            foreach (var entity in await _customPrograms.GetAllAsync())
            {
                if (ParseCustom(entity, bundled.Workouts) is { } program)
                {
                    custom.Add(program);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reading custom programs failed; showing the bundled catalog only.");
        }

        // Latest revisions first, so a renamed custom workout shows its newest name.
        // Revisions repeat their workouts; keep one each, from the latest revision.
        IEnumerable<Workout> customWorkouts = custom.Concat(_pinned.Values)
            .SelectMany(p => p.Workouts ?? [])
            .GroupBy(w => w.Id)
            .Select(g => g.First());
        WorkoutCatalog catalog = new(bundled.Version, [.. bundled.Workouts, .. customWorkouts], [.. bundled.Programs, .. custom]);
        return new(catalog, catalog.WorkoutsById);
    }

    private WorkoutProgram? ParseCustom(CustomProgramEntity entity, IReadOnlyList<Workout> bundledWorkouts)
    {
        if (CustomProgramFactory.Parse(entity) is not { } program)
        {
            _logger.LogWarning("Skipping unreadable custom program {SubjectKey} revision {Revision}.", entity.SubjectKey, entity.Revision);
            return null;
        }

        var placeholders = PlaceholdersForMissingWorkouts(program, bundledWorkouts);
        if (placeholders.Count > 0)
        {
            _logger.LogWarning(
                "Custom program '{ProgramId}' revision {Revision} uses workouts that left the catalog ({Ids}); showing them as '{Name}'.",
                program.Id,
                program.Revision,
                string.Join(", ", placeholders.Select(w => w.Id)),
                UnknownWorkoutName);
        }

        var errors = CatalogValidator.Validate(new WorkoutCatalog(BundledCatalogVersion, [.. bundledWorkouts, .. placeholders], [program]));
        if (errors.Count > 0)
        {
            _logger.LogWarning("Skipping invalid custom program '{ProgramId}': {Errors}", program.Id, string.Join(" ", errors));
            return null;
        }

        return placeholders.Count == 0 ? program : program with { Workouts = [.. program.Workouts ?? [], .. placeholders] };
    }

    // A catalog update must never lock a user out of their own schedule, even if it dropped a workout id.
    private static List<Workout> PlaceholdersForMissingWorkouts(WorkoutProgram program, IReadOnlyList<Workout> bundledWorkouts)
    {
        HashSet<string> known = [.. bundledWorkouts.Select(w => w.Id), .. (program.Workouts ?? []).Select(w => w.Id)];
        return
        [
            .. program.Tracks
                .SelectMany(t => t.Phases).SelectMany(p => p.Weeks).SelectMany(w => w.Days).SelectMany(d => d.Sessions)
                .SelectMany(s => (string?[])[s.WorkoutId, .. s.Alternates ?? []])
                .OfType<string>()
                .Where(id => !known.Contains(id))
                .Distinct()
                .Select(id => new Workout(id, UnknownWorkoutName, null, WorkoutKind.Mixed, null)),
        ];
    }

    private async Task<WorkoutCatalog> LoadBundledAsync()
    {
        var workouts = CatalogParser.ParseWorkouts(await _source.ReadWorkoutsAsync());
        List<WorkoutProgram> programs = new();

        foreach (var json in await _source.ReadProgramsAsync())
        {
            if (!CatalogParser.TryParseProgram(json, out var program, out var error))
            {
                _logger.LogWarning("Skipping unreadable catalog program: {Error}", error);
                continue;
            }

            if (program.IsCustom || IsCustomId(program.Id))
            {
                _logger.LogWarning("Skipping catalog program '{ProgramId}': custom programs only come from the builder.", program.Id);
                continue;
            }

            var errors = CatalogValidator.Validate(new WorkoutCatalog(BundledCatalogVersion, workouts, [program]));
            if (errors.Count > 0)
            {
                _logger.LogWarning("Skipping invalid catalog program '{ProgramId}': {Errors}", program.Id, string.Join(" ", errors));
                continue;
            }

            programs.Add(program);
        }

        return new(BundledCatalogVersion, workouts, programs);
    }

    private sealed record Merged(WorkoutCatalog Catalog, IReadOnlyDictionary<string, Workout> WorkoutsById);
}
