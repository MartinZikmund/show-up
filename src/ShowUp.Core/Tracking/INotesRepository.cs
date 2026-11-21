using ShowUp.Core.Storage;

namespace ShowUp.Core.Tracking;

public interface INotesRepository
{
    /// <summary>The enrollment's live notes: program (no day), day (no session) and session notes.</summary>
    Task<IReadOnlyList<NoteEntity>> GetNotesAsync(Guid enrollmentId);

    /// <summary>Upserts by (enrollment, day, session); blank text deletes the note.</summary>
    Task SetNoteAsync(Guid enrollmentId, int? dayIndex, int? sessionIndex, string? text);
}
