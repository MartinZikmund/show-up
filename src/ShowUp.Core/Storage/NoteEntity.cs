namespace ShowUp.Core.Storage;

/// <summary>Free text on a program (no day), a day (no session) or one session of a day.</summary>
public sealed class NoteEntity : SyncableEntity
{
    public Guid EnrollmentId { get; set; }

    public int? DayIndex { get; set; }

    public int? SessionIndex { get; set; }

    /// <summary><see cref="Tracking.SubjectKeys.Note"/>: the natural key the note is upserted by.</summary>
    public string SubjectKey { get; set; } = "";

    public string Text { get; set; } = "";
}
