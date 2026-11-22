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
            .GroupBy(n => n.SubjectKey)
            .Select(g => NewestFirst(g).First())
            .OrderBy(n => n.DayIndex ?? -1)
            .ThenBy(n => n.SessionIndex ?? -1)
            .ToList();
    }

    public async Task SetNotesAsync(Guid enrollmentId, IEnumerable<NoteEdit> notes)
    {
        List<(NoteEdit Edit, string Key, string Text)> edits =
        [
            .. notes.Select(n => (n, SubjectKeys.Note(enrollmentId, n.DayIndex, n.SessionIndex), Clean(n.Text))),
        ];

        var connection = await ConnectAsync();
        await connection.RunInTransactionAsync(c =>
        {
            foreach (var (edit, key, text) in edits)
            {
                var existing = NewestFirst(Query<NoteEntity>(c, n => n.SubjectKey == key)).ToList();
                if (text.Length == 0)
                {
                    Tombstone(c, existing);
                    continue;
                }

                if (existing.Count == 0)
                {
                    Insert(c, Stamp(new NoteEntity
                    {
                        EnrollmentId = enrollmentId,
                        DayIndex = edit.DayIndex,
                        SessionIndex = edit.SessionIndex,
                        SubjectKey = key,
                        Text = text,
                    }));
                    continue;
                }

                if (existing[0].Text != text)
                {
                    existing[0].Text = text;
                    Touch(c, existing[0]);
                }

                Tombstone(c, existing.Skip(1));
            }
        });
    }

    private static string Clean(string? text)
    {
        var trimmed = text?.Trim() ?? "";
        return trimmed.Length > MaxLength ? trimmed[..MaxLength] : trimmed;
    }
}
