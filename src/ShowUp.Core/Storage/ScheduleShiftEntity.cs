using SQLite;

namespace ShowUp.Core.Storage;

public sealed class ScheduleShiftEntity : SyncableEntity
{
    [Indexed]
    public Guid EnrollmentId { get; set; }

    public int FromDayNumber { get; set; }

    public int Days { get; set; }

    [Ignore]
    public DateOnly FromDate
    {
        get => DateOnly.FromDayNumber(FromDayNumber);
        set => FromDayNumber = value.DayNumber;
    }
}
