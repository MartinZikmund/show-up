using System.Text.RegularExpressions;

namespace ShowUp.Core.Catalog;

public static partial class CatalogValidator
{
    private const int MaxDaysPerWeek = 7;

    public static IReadOnlyList<string> Validate(WorkoutCatalog catalog)
    {
        List<string> errors = new();
        var allWorkouts = (catalog.Workouts ?? []).Where(w => w is not null).ToList();
        var allPrograms = (catalog.Programs ?? []).Where(p => p is not null).ToList();

        foreach (var dup in allWorkouts.GroupBy(w => w.Id).Where(g => g.Count() > 1))
        {
            errors.Add($"Duplicate workout id '{dup.Key}'.");
        }

        foreach (var workout in allWorkouts)
        {
            ValidateMetrics(workout, errors);
        }

        foreach (var dup in allPrograms.GroupBy(p => (p.Id, p.Revision)).Where(g => g.Count() > 1))
        {
            errors.Add($"Duplicate program id '{dup.Key.Id}' revision {dup.Key.Revision}.");
        }

        var workouts = allWorkouts
            .Where(w => w.Id is not null)
            .GroupBy(w => w.Id)
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var program in allPrograms)
        {
            ValidateProgram(program, workouts, errors);
        }

        return errors;
    }

    private static void ValidateMetrics(Workout workout, List<string> errors)
    {
        var metrics = (workout.Metrics ?? []).Where(m => m is not null).ToList();
        foreach (var metric in metrics.Where(m => string.IsNullOrWhiteSpace(m.Id) || string.IsNullOrWhiteSpace(m.Name)))
        {
            errors.Add($"Workout '{workout.Id}': metric '{metric.Id}' needs an id and a name.");
        }

        foreach (var dup in metrics.GroupBy(m => m.Id).Where(g => g.Count() > 1))
        {
            errors.Add($"Workout '{workout.Id}': duplicate metric id '{dup.Key}'.");
        }
    }

    private static void ValidateProgram(WorkoutProgram program, IReadOnlyDictionary<string, Workout> workouts, List<string> errors)
    {
        var p = $"Program '{program.Id}'";

        if (program.AccentColor is null || !AccentColorRegex().IsMatch(program.AccentColor))
        {
            errors.Add($"{p}: accent color '{program.AccentColor}' must look like #RRGGBB.");
        }

        var sources = program.Sources ?? [];
        var minSources = program.Verification == VerificationLevel.Official ? 1 : 2;
        if (sources.Count < minSources)
        {
            errors.Add($"{p}: {program.Verification} programs need at least {minSources} source(s), found {sources.Count}.");
        }

        var tracks = (program.Tracks ?? []).Where(t => t is not null).ToList();
        if (tracks.Count == 0)
        {
            errors.Add($"{p}: has no tracks.");
        }

        foreach (var track in tracks)
        {
            foreach (var phase in (track.Phases ?? []).Where(ph => ph is not null))
            {
                var weeks = phase.Weeks ?? [];
                for (var w = 0; w < weeks.Count; w++)
                {
                    var week = weeks[w];
                    var where = $"{p}, track '{track.Id}', phase '{phase.Name}', week {w + 1}";
                    var dayCount = week?.Days?.Count ?? 0;

                    if (dayCount is 0 or > MaxDaysPerWeek)
                    {
                        errors.Add($"{where}: has {dayCount} days, expected 1 to {MaxDaysPerWeek}.");
                    }

                    if (week?.Days is not null)
                    {
                        ValidateSessions(week, where, workouts, errors);
                    }
                }
            }
        }
    }

    private static void ValidateSessions(ScheduleWeek week, string where, IReadOnlyDictionary<string, Workout> workouts, List<string> errors)
    {
        for (var d = 0; d < week.Days.Count; d++)
        {
            var sessions = week.Days[d]?.Sessions ?? [];
            if (sessions.Count == 0)
            {
                errors.Add($"{where}, day {d + 1}: has no sessions.");
            }

            foreach (var session in sessions.Where(s => s is not null))
            {
                foreach (var id in (string?[])[session.WorkoutId, .. session.Alternates ?? []])
                {
                    if (id is null || !workouts.ContainsKey(id))
                    {
                        errors.Add($"{where}, day {d + 1}: unknown workout '{id}'.");
                    }
                }
            }
        }
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex AccentColorRegex();
}
