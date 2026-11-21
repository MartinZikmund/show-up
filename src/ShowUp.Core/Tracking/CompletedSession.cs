namespace ShowUp.Core.Tracking;

/// <param name="CompletedAt">When it was crossed; orders a day's completions for a stable marker seed.</param>
public sealed record CompletedSession(
    int DayIndex,
    int SessionIndex,
    Guid CompletionId,
    DateOnly CompletedOn,
    string? InkJson = null,
    DateTimeOffset CompletedAt = default);
