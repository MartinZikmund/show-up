namespace ShowUp.Core.Marking;

public sealed record CrossMomentInfo(
    int DayNumber,
    int TotalDays,
    string Title,
    int StrokesBefore,
    int StrokesAfter,
    int Seed,
    int SessionsDone,
    int SessionsRequired,
    int CurrentStreak);
