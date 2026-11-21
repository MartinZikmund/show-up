namespace ShowUp.Core.Marking;

/// <param name="LogResults">Opens "Log your results" once the moment is dismissed; null when the workout has nothing to log.</param>
public sealed record CrossMomentInfo(
    int DayNumber,
    int TotalDays,
    string Title,
    int StrokesBefore,
    int StrokesAfter,
    int Seed,
    int SessionsDone,
    int SessionsRequired,
    int CurrentStreak,
    Func<Task>? LogResults = null);
