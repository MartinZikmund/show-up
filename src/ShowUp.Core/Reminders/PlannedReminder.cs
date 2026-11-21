namespace ShowUp.Core.Reminders;

/// <summary>One notification to deliver: <paramref name="FireAt"/> carries the offset in force at that wall-clock time.</summary>
public sealed record PlannedReminder(Guid ReminderId, DateTimeOffset FireAt, string Title, string Body);

/// <summary>What a date asks of the user across every active program.</summary>
/// <param name="Completed">Everything due is crossed, or nothing is due: no reminder.</param>
/// <param name="WorkoutTitle">Name of the first open session, "+ N more" when several.</param>
/// <param name="IsRest">Only rest days are due.</param>
public sealed record ReminderDay(bool Completed, string? WorkoutTitle, bool IsRest);
