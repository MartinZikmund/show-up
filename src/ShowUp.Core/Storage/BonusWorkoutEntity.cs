using SQLite;

namespace ShowUp.Core.Storage;

public sealed class BonusWorkoutEntity : SyncableEntity
{
    public string? WorkoutId { get; set; }

    public string Title { get; set; } = "";

    public int DayNumber { get; set; }

    public int? DurationMinutes { get; set; }

    [Ignore]
    public DateOnly Date
    {
        get => DateOnly.FromDayNumber(DayNumber);
        set => DayNumber = value.DayNumber;
    }
}
