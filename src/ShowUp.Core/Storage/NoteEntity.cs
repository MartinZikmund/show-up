namespace ShowUp.Core.Storage;

/// <summary>Free text on a program (no day), a day (no session) or one session of a day.</summary>
public sealed class NoteEntity : SyncableEntity
{
    public Guid EnrollmentId { get; set; }

    public int? DayIndex { get; set; }

    public int? SessionIndex { get; set; }

    public string Text { get; set; } = "";
}
