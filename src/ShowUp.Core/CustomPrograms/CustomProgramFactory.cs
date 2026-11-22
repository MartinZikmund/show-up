using System.Globalization;
using System.Text.Json;
using ShowUp.Core.Catalog;
using ShowUp.Core.Storage;

namespace ShowUp.Core.CustomPrograms;

public sealed record CustomSession(string WorkoutId, bool Optional = false);

/// <summary>What the builder edits: weeks of seven days, each a list of sessions; <see cref="Workouts"/> are the user's own.</summary>
public sealed record CustomProgramDraft(
    string ProgramId,
    string Name,
    string AccentColor,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<CustomSession>>> Weeks,
    IReadOnlyList<Workout> Workouts);

public enum CustomProgramProblem
{
    EmptyName,
    NoWeeks,
    TooManyWeeks,
    EmptyDay,
    TooLarge,
}

/// <param name="Week">1-based, for <see cref="CustomProgramProblem.EmptyDay"/>.</param>
/// <param name="Day">1-based, for <see cref="CustomProgramProblem.EmptyDay"/>.</param>
public sealed record CustomProgramError(CustomProgramProblem Problem, int Week = 0, int Day = 0);

/// <summary>Turns builder drafts into program JSON and back.</summary>
public static class CustomProgramFactory
{
    public const int MinWeeks = 1;
    public const int MaxWeeks = 26;
    public const int DaysPerWeek = 7;
    public const int MaxNameLength = 60;

    /// <summary>Stored program JSON; the server rejects a bigger row, which would fail the sync forever.</summary>
    public const int MaxJsonBytes = 256 * 1024;
    public const string RestWorkoutId = "rest";
    public const string TrackId = "main";

    /// <summary>Every custom program id, and so every custom workout id, starts with this.</summary>
    public const string IdPrefix = "custom-";

    public static bool IsCustomId(string id) => id.StartsWith(IdPrefix, StringComparison.Ordinal);

    public static string NewProgramId() => $"{IdPrefix}{Guid.NewGuid():D}";

    /// <summary><c>{programId}-{n}</c>, one past the highest number used so far (ids are never reused).</summary>
    public static string NextWorkoutId(string programId, IEnumerable<Workout> existing)
    {
        string prefix = $"{programId}-";
        int max = existing
            .Select(w => w.Id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(w.Id.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"{prefix}{max + 1}";
    }

    public static IReadOnlyList<CustomProgramError> Validate(CustomProgramDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        List<CustomProgramError> errors = [];
        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            errors.Add(new(CustomProgramProblem.EmptyName));
        }

        if (draft.Weeks.Count < MinWeeks)
        {
            errors.Add(new(CustomProgramProblem.NoWeeks));
        }
        else if (draft.Weeks.Count > MaxWeeks)
        {
            errors.Add(new(CustomProgramProblem.TooManyWeeks));
        }

        for (int w = 0; w < draft.Weeks.Count; w++)
        {
            for (int d = 0; d < draft.Weeks[w].Count; d++)
            {
                if (draft.Weeks[w][d].Count == 0)
                {
                    errors.Add(new(CustomProgramProblem.EmptyDay, w + 1, d + 1));
                }
            }
        }

        if (errors.Count == 0 && System.Text.Encoding.UTF8.GetByteCount(Serialize(Build(draft, DateOnly.MinValue))) > MaxJsonBytes)
        {
            errors.Add(new(CustomProgramProblem.TooLarge));
        }

        return errors;
    }

    /// <summary>
    /// The program as stored; id and revision are the repository's (the JSON keeps placeholders). Every workout the program
    /// ever had stays in <see cref="WorkoutProgram.Workouts"/>, so older revisions pinned by enrollments still find theirs.
    /// </summary>
    public static WorkoutProgram Create(CustomProgramDraft draft, DateOnly savedOn)
    {
        if (Validate(draft) is { Count: > 0 } errors)
        {
            throw new ArgumentException($"The schedule isn't valid: {string.Join(", ", errors)}.", nameof(draft));
        }

        return Build(draft, savedOn);
    }

    private static WorkoutProgram Build(CustomProgramDraft draft, DateOnly savedOn)
    {
        string trimmed = draft.Name.Trim();
        string name = trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed;
        List<ScheduleWeek> weeks =
        [
            .. draft.Weeks.Select(week => new ScheduleWeek(
            [
                .. week.Select(day => new ScheduleDay([.. day.Select(s => new ScheduledSession(s.WorkoutId, Optional: s.Optional))])),
            ])),
        ];

        return new WorkoutProgram(
            draft.ProgramId,
            0,
            name,
            "",
            "",
            null,
            draft.AccentColor,
            VerificationLevel.Custom,
            [],
            savedOn,
            [new ProgramTrack(TrackId, name, [new ProgramPhase(name, weeks)])],
            [.. draft.Workouts]);
    }

    public static CustomProgramDraft ToDraft(WorkoutProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var track = program.Tracks.FirstOrDefault();
        IReadOnlyList<IReadOnlyList<IReadOnlyList<CustomSession>>> weeks = track is null
            ? []
            :
            [
                .. track.Phases.SelectMany(p => p.Weeks).Select(week => (IReadOnlyList<IReadOnlyList<CustomSession>>)
                [
                    .. week.Days.Select(day => (IReadOnlyList<CustomSession>)[.. day.Sessions.Select(s => new CustomSession(s.WorkoutId, s.Optional))]),
                ]),
            ];
        return new(program.Id, program.Name, program.AccentColor, weeks, program.Workouts ?? []);
    }

    public static string Serialize(WorkoutProgram program) =>
        JsonSerializer.Serialize(program, CatalogJsonContext.Default.WorkoutProgram);

    /// <summary>The stored program with the row's id and revision, or null when the JSON can't be read.</summary>
    public static WorkoutProgram? Parse(CustomProgramEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return entity.ProgramId is { } id && CatalogParser.TryParseProgram(entity.ProgramJson, out var program, out _)
            ? program with { Id = id, Revision = entity.Revision, Verification = VerificationLevel.Custom }
            : null;
    }
}
