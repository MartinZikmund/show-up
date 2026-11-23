using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ShowUp.Core.CustomPrograms;
using ShowUp.Core.Messages;
using ShowUp.Core.Storage;
using ShowUp.Core.Supabase;

namespace ShowUp.Core.Catalog;

/// <summary>The bundled catalog plus what was downloaded (read once) merged with the user's custom programs (re-read after <see cref="InvalidateCustomPrograms"/>).</summary>
public sealed class CatalogService : ICatalogService, ICatalogUpdateTarget
{
    public const int BundledCatalogVersion = 1;

    public const string UnknownWorkoutName = "Unknown workout";

    private readonly IBundledCatalogSource _source;
    private readonly ICustomProgramRepository _customPrograms;
    private readonly ILogger<CatalogService> _logger;
    private readonly ICatalogCache? _cache;
    private readonly string? _heroHost;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConditionalWeakTable<ProgramTrack, IReadOnlyList<ScheduleDay>> _days = new();

    // Older or deleted custom revisions that enrollments pinned; their workouts join the merged catalog.
    private readonly ConcurrentDictionary<(string Id, int Revision), WorkoutProgram> _pinned = new();
    private Loaded? _bundled;
    private volatile Merged? _merged;

    // Bumped by every invalidation, so a merge that raced one isn't cached.
    private int _generation;

    // Bumped by full invalidations only, so a bundled read that raced one isn't cached.
    private int _bundledGeneration;

    public CatalogService(IBundledCatalogSource source, ICustomProgramRepository customPrograms, ILogger<CatalogService> logger, IMessenger? messenger = null, ICatalogCache? cache = null, SupabaseOptions? options = null)
    {
        _cache = cache;
        _heroHost = options is { IsConfigured: true } ? new Uri(options.Url).Host : null;
        _source = source;
        _customPrograms = customPrograms;
        _logger = logger;
        messenger?.Register<CatalogService, DataChangedMessage>(this, static (r, _) => r.Invalidate());
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _bundledGeneration);
        Volatile.Write(ref _bundled, null);
        _pinned.Clear();
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

        if ((await GetBundledAsync()).Archive.TryGetValue((programId, r), out var archived))
        {
            return archived;
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
            var merged = await MergeAsync(bundled.Catalog);
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
            || ParseCustom(entity, (await GetBundledAsync()).Catalog.Workouts) is not { } program)
        {
            return null;
        }

        if (_pinned.TryAdd((programId, revision), program))
        {
            InvalidateCustomPrograms();
        }

        return program;
    }

    private async Task<Loaded> GetBundledAsync()
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
    private async Task<Loaded> GetBundledLockedAsync()
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

    public async Task<int> GetInstalledVersionAsync() => (await GetBundledAsync()).Catalog.Version;

    public async Task<bool> ApplyAsync(RemoteCatalog remote)
    {
        if (_cache is null)
        {
            return false;
        }

        await _gate.WaitAsync();
        try
        {
            var current = await GetBundledLockedAsync();
            if (remote.Version <= current.Catalog.Version)
            {
                return false;
            }

            var (workouts, workoutsJson, workoutsAccepted) = AcceptWorkouts(remote, current);
            List<CachedProgram> accepted = [];
            List<WorkoutProgram> added = [];
            foreach (var row in remote.Programs)
            {
                if (current.Archive.ContainsKey((row.Id, row.Revision)))
                {
                    continue;
                }

                if (TryAcceptProgram(row.Json, workouts, out bool heroStripped) is { } program && program.Id == row.Id && program.Revision == row.Revision)
                {
                    accepted.Add(new(row.Id, row.Revision, remote.Version, row.Json));
                    added.Add(heroStripped ? WithFallbackHero(program, [.. current.All, .. added]) : program);
                }
                else
                {
                    _logger.LogWarning("Skipping program '{ProgramId}' revision {Revision} from catalog version {Version}; keeping what is installed.", row.Id, row.Revision, remote.Version);
                }
            }

            // Rejected workouts: the version is not recorded, so a corrected publish is looked at again.
            int version = workoutsAccepted ? remote.Version : current.Catalog.Version;
            if (!workoutsAccepted && accepted.Count == 0)
            {
                return false;
            }

            try
            {
                await _cache.SaveAsync(version, workoutsJson, accepted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Saving catalog version {Version} failed; keeping the installed catalog.", remote.Version);
                throw;
            }

            // Only now does memory change: the cache and the catalog never disagree.
            var loaded = BuildLoaded(version, workouts, [.. current.All, .. added], workoutsJson);
            Interlocked.Increment(ref _bundledGeneration);
            Volatile.Write(ref _bundled, loaded);
            InvalidateCustomPrograms();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    // The downloaded workouts replace the current ones only if every id known today is still there.
    private (IReadOnlyList<Workout> Workouts, string? Json, bool Accepted) AcceptWorkouts(RemoteCatalog remote, Loaded current)
    {
        IReadOnlyList<Workout> keep = current.Catalog.Workouts;
        try
        {
            string json = "[" + string.Join(',', remote.Workouts) + "]";
            var incoming = CatalogParser.ParseWorkouts(json);
            var ids = incoming.Select(w => w.Id).ToHashSet();
            var removed = keep.Select(w => w.Id).Where(id => !ids.Contains(id)).ToList();
            var errors = CatalogValidator.Validate(new WorkoutCatalog(remote.Version, incoming, []));
            if (removed.Count == 0 && errors.Count == 0)
            {
                return (incoming, json, true);
            }

            _logger.LogWarning(
                "Rejecting the workouts of catalog version {Version}: removed ids [{Removed}] {Errors}",
                remote.Version,
                string.Join(", ", removed),
                string.Join(" ", errors));
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "Rejecting the unreadable workouts of catalog version {Version}.", remote.Version);
        }

        return (keep, current.CachedWorkoutsJson, false);
    }

    private WorkoutProgram? TryAcceptProgram(string json, IReadOnlyList<Workout> workouts, out bool heroStripped)
    {
        heroStripped = false;
        if (!CatalogParser.TryParseProgram(json, out var program, out var error))
        {
            _logger.LogWarning("Skipping unreadable catalog program: {Error}", error);
            return null;
        }

        if (program.IsCustom || IsCustomId(program.Id))
        {
            _logger.LogWarning("Skipping catalog program '{ProgramId}': custom programs only come from the builder.", program.Id);
            return null;
        }

        var errors = CatalogValidator.Validate(new WorkoutCatalog(BundledCatalogVersion, workouts, [program]));
        if (errors.Count > 0)
        {
            _logger.LogWarning("Skipping invalid catalog program '{ProgramId}': {Errors}", program.Id, string.Join(" ", errors));
            return null;
        }

        if (!HeroImagePolicy.IsAllowed(program.HeroImage, _heroHost))
        {
            _logger.LogWarning("Dropping the hero image of '{ProgramId}' revision {Revision}: only https URLs on the Supabase host are loaded.", program.Id, program.Revision);
            heroStripped = true;
            return program with { HeroImage = null };
        }

        return program;
    }

    // A dropped hero falls back to the art an earlier revision of the same program had (the bundled one).
    private static WorkoutProgram WithFallbackHero(WorkoutProgram program, IEnumerable<WorkoutProgram> known) => program with
    {
        HeroImage = known.Where(p => p.Id == program.Id && p.HeroImage is not null).OrderByDescending(p => p.Revision).FirstOrDefault()?.HeroImage,
    };

    private async Task<Loaded> LoadBundledAsync()
    {
        IReadOnlyList<Workout> workouts = CatalogParser.ParseWorkouts(await _source.ReadWorkoutsAsync());
        List<WorkoutProgram> programs = [];
        HashSet<(string, int)> seen = [];

        foreach (var json in await _source.ReadProgramsAsync())
        {
            if (TryAcceptProgram(json, workouts, out _) is { } program && seen.Add((program.Id, program.Revision)))
            {
                programs.Add(program);
            }
        }

        int version = BundledCatalogVersion;
        string? cachedWorkoutsJson = null;
        if (await ReadCacheAsync() is { } cached)
        {
            if (cached.WorkoutsJson is { } cachedJson && TryUseCachedWorkouts(cachedJson, workouts) is { } cachedWorkouts)
            {
                workouts = cachedWorkouts;
                cachedWorkoutsJson = cachedJson;
            }

            foreach (var row in cached.Programs.OrderBy(p => p.Id, StringComparer.Ordinal).ThenBy(p => p.Revision))
            {
                if (!seen.Contains((row.Id, row.Revision))
                    && TryAcceptProgram(row.Json, workouts, out bool heroStripped) is { } program
                    && seen.Add((program.Id, program.Revision)))
                {
                    programs.Add(heroStripped ? WithFallbackHero(program, programs) : program);
                }
            }

            version = Math.Max(version, cached.Version);
        }

        return BuildLoaded(version, workouts, programs, cachedWorkoutsJson);
    }

    // One card per program: the newest revision. Older ones stay resolvable through the archive.
    private static Loaded BuildLoaded(int version, IReadOnlyList<Workout> workouts, IReadOnlyList<WorkoutProgram> all, string? cachedWorkoutsJson)
    {
        Dictionary<(string Id, int Revision), WorkoutProgram> archive = [];
        foreach (var program in all)
        {
            archive.TryAdd((program.Id, program.Revision), program);
        }

        List<WorkoutProgram> latest = [.. all.GroupBy(p => p.Id).Select(g => g.OrderByDescending(p => p.Revision).First())];
        return new(new WorkoutCatalog(version, workouts, latest), archive, cachedWorkoutsJson, all);
    }

    private async Task<CachedCatalog?> ReadCacheAsync()
    {
        if (_cache is null)
        {
            return null;
        }

        try
        {
            return await _cache.LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reading the downloaded catalog failed; using the bundled one.");
            return null;
        }
    }

    private IReadOnlyList<Workout>? TryUseCachedWorkouts(string json, IReadOnlyList<Workout> bundled)
    {
        try
        {
            var cached = CatalogParser.ParseWorkouts(json);
            var ids = cached.Select(w => w.Id).ToHashSet();
            if (bundled.All(w => ids.Contains(w.Id)))
            {
                return cached;
            }

            _logger.LogWarning("Ignoring the downloaded workouts: they drop bundled ids.");
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "Ignoring the unreadable downloaded workouts.");
        }

        return null;
    }

    private sealed record Loaded(
        WorkoutCatalog Catalog,
        IReadOnlyDictionary<(string Id, int Revision), WorkoutProgram> Archive,
        string? CachedWorkoutsJson,
        IReadOnlyList<WorkoutProgram> All);

    private sealed record Merged(WorkoutCatalog Catalog, IReadOnlyDictionary<string, Workout> WorkoutsById);
}
