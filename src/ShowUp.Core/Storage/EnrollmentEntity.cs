using SQLite;

namespace ShowUp.Core.Storage;

public enum EnrollmentEndReason
{
    Finished,
    Abandoned,
}

public sealed class EnrollmentEntity : SyncableEntity
{
    public string ProgramId { get; set; } = "";

    public int ProgramRevision { get; set; }

    public string TrackId { get; set; } = "";

    public int StartDayNumber { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public EnrollmentEndReason? EndReason { get; set; }

    [Ignore]
    public DateOnly StartDate
    {
        get => DateOnly.FromDayNumber(StartDayNumber);
        set => StartDayNumber = value.DayNumber;
    }
}
