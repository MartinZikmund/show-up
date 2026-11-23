using ShowUp.Core.Tracking;

namespace ShowUp.Core.Marking;

/// <param name="LogResults">Opens "Log your results" once the moment is dismissed; null when the workout has nothing to log.</param>
/// <param name="Milestone">The week, phase or program this cross completed, if any.</param>
/// <param name="Finish">Ends the program as finished; offered only when this cross completed it.</param>
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
    Func<Task>? LogResults = null,
    Milestone? Milestone = null,
    Func<Task>? Finish = null);
