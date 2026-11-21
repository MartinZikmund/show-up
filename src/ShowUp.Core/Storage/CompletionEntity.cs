using SQLite;

namespace ShowUp.Core.Storage;

public sealed class CompletionEntity : SyncableEntity
{
    [Indexed(Name = "IX_Completion_Session", Order = 1)]
    public Guid EnrollmentId { get; set; }

    [Indexed(Name = "IX_Completion_Session", Order = 2)]
    public int DayIndex { get; set; }

    [Indexed(Name = "IX_Completion_Session", Order = 3)]
    public int SessionIndex { get; set; }

    public int CompletedOnDayNumber { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    [Ignore]
    public DateOnly CompletedOn
    {
        get => DateOnly.FromDayNumber(CompletedOnDayNumber);
        set => CompletedOnDayNumber = value.DayNumber;
    }
}
