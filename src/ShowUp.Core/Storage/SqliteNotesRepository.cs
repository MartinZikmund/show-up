using ShowUp.Core.Tracking;

namespace ShowUp.Core.Storage;

public sealed class SqliteNotesRepository(ShowUpDatabase db, TimeProvider time, IUserScope user, IDeviceIdProvider device)
    : SqliteRepositoryBase(db, time, user, device), INotesRepository
{
    public const int MaxLength = 4000;

    public async Task<IReadOnlyList<NoteEntity>> GetNotesAsync(Guid enrollmentId)
    {
        var connection = await ConnectAsync();
        var rows = await QueryAsync<NoteEntity>(connection, n => n.EnrollmentId == enrollmentId);
        return rows
            .GroupBy(n => (n.DayIndex, n.SessionIndex))
            .Select(g => g.OrderByDescending(n => n.UpdatedAt).First())
            .OrderBy(n => n.DayIndex ?? -1)
            .ThenBy(n => n.SessionIndex ?? -1)
            .ToList();
    }

    public async Task SetNoteAsync(Guid enrollmentId, int? dayIndex, int? sessionIndex, string? text)
    {
        if (sessionIndex is not null && dayIndex is null)
        {
            throw new ArgumentException("A session note needs its day.", nameof(sessionIndex));
        }

        var trimmed = text?.Trim() ?? "";
        if (trimmed.Length > MaxLength)
        {
            trimmed = trimmed[..MaxLength];
        }

        var connection = await ConnectAsync();

        // Null keys can't be matched in SQL with a parameter, so the day/session filter runs here.
        var existing = (await QueryAsync<NoteEntity>(connection, n => n.EnrollmentId == enrollmentId))
            .Where(n => n.DayIndex == dayIndex && n.SessionIndex == sessionIndex)
            .OrderByDescending(n => n.UpdatedAt)
            .ToList();

        if (trimmed.Length == 0)
        {
            await TombstoneDuplicatesAsync(connection, existing);
            return;
        }

        if (existing.Count == 0)
        {
            await connection.InsertAsync(Stamp(new NoteEntity
            {
                EnrollmentId = enrollmentId,
                DayIndex = dayIndex,
                SessionIndex = sessionIndex,
                Text = trimmed,
            }));
            return;
        }

        var keep = existing[0];
        if (keep.Text != trimmed)
        {
            keep.Text = trimmed;
            await TouchAsync(connection, keep);
        }

        await TombstoneDuplicatesAsync(connection, existing.Skip(1));
    }
}
