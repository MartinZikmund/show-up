using System.Text.RegularExpressions;

namespace ShowUp.Core.Catalog;

public static partial class CatalogValidator
{
    private const int MaxDaysPerWeek = 7;

    public static IReadOnlyList<string> Validate(WorkoutCatalog catalog)
    {
        List<string> errors = new();

        foreach (var dup in catalog.Workouts.GroupBy(w => w.Id).Where(g => g.Count() > 1))
        {
            errors.Add($"Duplicate workout id '{dup.Key}'.");
        }

        foreach (var dup in catalog.Programs.GroupBy(p => p.Id).Where(g => g.Count() > 1))
        {
            errors.Add($"Duplicate program id '{dup.Key}'.");
        }

        var workouts = catalog.WorkoutsById;
        foreach (var program in catalog.Programs)
        {
            ValidateProgram(program, workouts, errors);
        }

        return errors;
    }

    private static void ValidateProgram(WorkoutProgram program, IReadOnlyDictionary<string, Workout> workouts, List<string> errors)
    {
        var p = $"Program '{program.Id}'";

        if (!AccentColorRegex().IsMatch(program.AccentColor))
        {
            errors.Add($"{p}: accent color '{program.AccentColor}' must look like #RRGGBB.");
        }

        var minSources = program.Verification == VerificationLevel.Official ? 1 : 2;
        if (program.Sources.Count < minSources)
        {
            errors.Add($"{p}: {program.Verification} programs need at least {minSources} source(s), found {program.Sources.Count}.");
        }

        if (program.Tracks.Count == 0)
        {
            errors.Add($"{p}: has no tracks.");
        }

        foreach (var track in program.Tracks)
        {
            foreach (var phase in track.Phases)
            {
                for (var w = 0; w < phase.Weeks.Count; w++)
                {
                    var week = phase.Weeks[w];
                    var where = $"{p}, track '{track.Id}', phase '{phase.Name}', week {w + 1}";

                    if (week.Days.Count is 0 or > MaxDaysPerWeek)
                    {
                        errors.Add($"{where}: has {week.Days.Count} days, expected 1 to {MaxDaysPerWeek}.");
                    }

                    ValidateSessions(week, where, workouts, errors);
                }
            }
        }
    }

    private static void ValidateSessions(ScheduleWeek week, string where, IReadOnlyDictionary<string, Workout> workouts, List<string> errors)
    {
        for (var d = 0; d < week.Days.Count; d++)
        {
            var sessions = week.Days[d].Sessions;
            if (sessions.Count == 0)
            {
                errors.Add($"{where}, day {d + 1}: has no sessions.");
            }

            foreach (var session in sessions)
            {
                foreach (var id in (string[])[session.WorkoutId, .. session.Alternates ?? []])
                {
                    if (!workouts.ContainsKey(id))
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
