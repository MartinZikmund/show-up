using System.Text.Json;
using ShowUp.Core.Catalog;

namespace PublishCatalog;

public sealed partial record ServerProgram(string Id, int Revision, JsonElement Json);

/// <summary>What is on the server now. <see cref="Version"/> is 0 for an empty project; <see cref="MaxRowVersion"/> is the highest version any workout or program row carries (above <see cref="Version"/> after a failed run).</summary>
public sealed record ServerState(int Version, IReadOnlyDictionary<string, JsonElement> Workouts, IReadOnlyList<ServerProgram> Programs, int MaxRowVersion = 0);

public sealed record PublishPlan(
    int NewVersion,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> NewWorkouts,
    IReadOnlyList<string> ChangedWorkouts,
    IReadOnlyList<LocalProgram> NewPrograms,
    IReadOnlyList<string> Warnings,
    bool Recovering = false)
{
    public bool IsRefused => Errors.Count > 0;

    public bool HasChanges => Recovering || NewWorkouts.Count + ChangedWorkouts.Count + NewPrograms.Count > 0;

    /// <summary>Only new and changed workouts move to the new version; the rest stay visible at their old one.</summary>
    public bool WritesWorkout(string id) => NewWorkouts.Contains(id) || ChangedWorkouts.Contains(id);

    public IEnumerable<HeroUpload> Heroes => NewPrograms.Select(p => p.Hero).OfType<HeroUpload>().DistinctBy(h => h.ObjectName);
}

public static class CatalogPlanner
{
    public static PublishPlan Plan(LocalCatalog local, ServerState server)
    {
        List<string> errors = new();
        var localIds = local.Workouts.Select(w => w.Id).ToHashSet();

        foreach (var id in local.LockedWorkoutIds.Where(id => !localIds.Contains(id)).Order(StringComparer.Ordinal))
        {
            errors.Add($"Workout id '{id}' is in catalog/workout-ids.lock but gone from workouts.json. Ids are append-only: restore it.");
        }

        foreach (var id in server.Workouts.Keys.Where(id => !localIds.Contains(id) && !local.LockedWorkoutIds.Contains(id)).Order(StringComparer.Ordinal))
        {
            errors.Add($"Workout id '{id}' is published on the server but gone from workouts.json. Ids are append-only: restore it.");
        }

        List<string> newWorkouts = new();
        List<string> changedWorkouts = new();
        foreach (var workout in local.Workouts)
        {
            if (!server.Workouts.TryGetValue(workout.Id, out var published))
            {
                newWorkouts.Add(workout.Id);
            }
            else if (!JsonCompare.Equal(workout.Json, published))
            {
                changedWorkouts.Add(workout.Id);
            }
        }

        List<LocalProgram> newPrograms = new();
        foreach (var program in local.Programs)
        {
            var published = server.Programs.Where(p => p.Id == program.Id).ToList();
            if (published.FirstOrDefault(p => p.Revision == program.Revision) is { } same)
            {
                if (!JsonCompare.Equal(program.Json, same.Json))
                {
                    errors.Add($"Program '{program.Id}' revision {program.Revision} is published and its JSON changed. Bump \"revision\" (apps never replace a revision they already hold).");
                }
            }
            else if (published.Count > 0 && program.Revision < published.Max(p => p.Revision))
            {
                errors.Add($"Program '{program.Id}' is at revision {program.Revision} but revision {published.Max(p => p.Revision)} is already published. Revisions only go up.");
            }
            else
            {
                newPrograms.Add(program);
            }
        }

        // Programs are never removed from the server, so a missing local file is allowed but worth a look.
        var localProgramIds = local.Programs.Select(p => p.Id).ToHashSet();
        List<string> warnings = [.. server.Programs.Select(p => p.Id).Distinct().Where(id => !localProgramIds.Contains(id)).Order(StringComparer.Ordinal)
            .Select(id => $"Program '{id}' is published but has no file in catalog/programs/. It stays on the server.")];

        // Rows above the recorded version are orphans of a failed run: finish that version instead of skipping past it.
        bool recovering = server.MaxRowVersion > server.Version;
        return new(recovering ? server.MaxRowVersion : Math.Max(server.Version, CatalogService.BundledCatalogVersion) + 1, errors, newWorkouts, changedWorkouts, newPrograms, warnings, recovering);
    }

    public static string Describe(PublishPlan plan) =>
        string.Concat(plan.Warnings.Select(w => $"Warning: {w}{Environment.NewLine}")) + DescribeBody(plan);

    private static string DescribeBody(PublishPlan plan)
    {
        if (plan.IsRefused)
        {
            return "Refused:\n" + string.Join('\n', plan.Errors.Select(e => "  - " + e));
        }

        if (!plan.HasChanges)
        {
            return "Nothing changed since the published catalog: no new version.";
        }

        List<string> lines = [plan.Recovering
            ? $"Publish catalog version {plan.NewVersion} (recovering version {plan.NewVersion}: a previous run left its rows without the version record):"
            : $"Publish catalog version {plan.NewVersion}:"];
        lines.Add($"  workouts: write {plan.NewWorkouts.Count + plan.ChangedWorkouts.Count} at version {plan.NewVersion} ({plan.NewWorkouts.Count} new, {plan.ChangedWorkouts.Count} changed)");
        foreach (var id in plan.NewWorkouts)
        {
            lines.Add($"    + {id}");
        }

        foreach (var id in plan.ChangedWorkouts)
        {
            lines.Add($"    ~ {id}");
        }

        foreach (var hero in plan.Heroes)
        {
            lines.Add($"  hero: upload {hero.ObjectName} ({hero.ContentType}) if missing");
        }

        foreach (var program in plan.NewPrograms)
        {
            lines.Add($"  program: add {program.Id} revision {program.Revision}");
        }

        lines.Add($"  then record catalog_versions {plan.NewVersion}");
        return string.Join('\n', lines);
    }
}
