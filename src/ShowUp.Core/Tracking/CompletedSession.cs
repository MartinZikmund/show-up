namespace ShowUp.Core.Tracking;

public sealed record CompletedSession(int DayIndex, int SessionIndex, Guid CompletionId, DateOnly CompletedOn);
