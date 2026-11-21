using ShowUp.Core.Storage;

namespace ShowUp.Core.Tracking;

/// <summary>A note to write: program (no day), day (no session) or session; blank text deletes it.</summary>
public sealed record NoteEdit(int? DayIndex, int? SessionIndex, string? Text);

public interface INotesRepository
{
    /// <summary>The enrollment's live notes: program (no day), day (no session) and session notes.</summary>
    Task<IReadOnlyList<NoteEntity>> GetNotesAsync(Guid enrollmentId);

    /// <summary>Upserts each note by its subject key in one transaction; blank text deletes the note.</summary>
    Task SetNotesAsync(Guid enrollmentId, IEnumerable<NoteEdit> notes);
}
