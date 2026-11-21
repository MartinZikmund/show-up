using System.Text.Json.Serialization;

namespace ShowUp.Core.Catalog;

public sealed record ProgramPhase(string Name, IReadOnlyList<ScheduleWeek> Weeks);

public sealed partial record ProgramTrack(string Id, string Name, IReadOnlyList<ProgramPhase> Phases)
{
    /// <summary>All days in phase, week, day order.</summary>
    [JsonIgnore]
    public IReadOnlyList<ScheduleDay> Days => [.. Phases.SelectMany(p => p.Weeks).SelectMany(w => w.Days)];

    /// <summary>Phase name, 1-based week across the track, and 1-based day within that week.</summary>
    public (string PhaseName, int WeekNumber, int DayOfWeek) Locate(int dayIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dayIndex);

        int remaining = dayIndex;
        int weekNumber = 0;
        foreach (ProgramPhase phase in Phases)
        {
            foreach (ScheduleWeek week in phase.Weeks)
            {
                weekNumber++;
                if (remaining < week.Days.Count)
                {
                    return (phase.Name, weekNumber, remaining + 1);
                }

                remaining -= week.Days.Count;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(dayIndex));
    }
}

public sealed record CatalogSource(string Url, string Confirms);

public sealed partial record WorkoutProgram(
    string Id,
    int Revision,
    string Name,
    string Family,
    string Description,
    string? HeroImage,
    string AccentColor,
    VerificationLevel Verification,
    IReadOnlyList<CatalogSource> Sources,
    DateOnly VerifiedOn,
    IReadOnlyList<ProgramTrack> Tracks,
    IReadOnlyList<Workout>? Workouts = null)
{
    /// <summary>The user's own workouts, carried inline; only custom programs have them.</summary>
    [JsonIgnore]
    public bool IsCustom => Verification == VerificationLevel.Custom;
}

public sealed record WorkoutCatalog(int Version, IReadOnlyList<Workout> Workouts, IReadOnlyList<WorkoutProgram> Programs)
{
    /// <summary>First workout wins when ids are duplicated (the validator reports those).</summary>
    public IReadOnlyDictionary<string, Workout> WorkoutsById =>
        Workouts.GroupBy(w => w.Id).ToDictionary(g => g.Key, g => g.First());
}
