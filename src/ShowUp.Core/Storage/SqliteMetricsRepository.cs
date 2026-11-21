using ShowUp.Core.Tracking;

namespace ShowUp.Core.Storage;

public sealed class SqliteMetricsRepository(ShowUpDatabase db, TimeProvider time, IUserScope user, IDeviceIdProvider device)
    : SqliteRepositoryBase(db, time, user, device), IMetricsRepository
{
    public const int MaxTextLength = 200;

    public async Task<IReadOnlyList<MetricEntryEntity>> GetSessionEntriesAsync(Guid enrollmentId)
    {
        Guid? id = enrollmentId;
        var connection = await ConnectAsync();
        var rows = await QueryAsync<MetricEntryEntity>(connection, m => m.EnrollmentId == id);
        return Newest(rows.Where(m => m.BonusWorkoutId is null));
    }

    public async Task<IReadOnlyList<MetricEntryEntity>> GetBonusEntriesAsync(IReadOnlyCollection<Guid> bonusWorkoutIds)
    {
        if (bonusWorkoutIds.Count == 0)
        {
            return [];
        }

        var connection = await ConnectAsync();
        var rows = await QueryAsync<MetricEntryEntity>(connection, m => m.BonusWorkoutId != null);
        HashSet<Guid> wanted = [.. bonusWorkoutIds];
        return Newest(rows.Where(m => m.BonusWorkoutId is { } b && wanted.Contains(b)));
    }

    public async Task SetValuesAsync(MetricTarget target, IReadOnlyDictionary<string, MetricValue> values)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(values);
        foreach (var (metricId, value) in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(metricId);
            if (value.Number is { } number && !double.IsFinite(number))
            {
                throw new ArgumentOutOfRangeException(nameof(values), number, "A result must be a finite number.");
            }
        }

        var key = target.SubjectKey;
        var connection = await ConnectAsync();
        await connection.RunInTransactionAsync(c =>
        {
            var rows = Query<MetricEntryEntity>(c, m => m.SubjectKey == key).Where(target.Matches).ToList();
            foreach (var (metricId, value) in values)
            {
                Set(c, target, rows.Where(m => m.MetricId == metricId).OrderByDescending(m => m.UpdatedAt).ToList(), metricId, value);
            }
        });
    }

    private void Set(SQLite.SQLiteConnection c, MetricTarget target, List<MetricEntryEntity> existing, string metricId, MetricValue value)
    {
        var text = value.Text?.Trim() is { Length: > 0 } t ? t[..Math.Min(t.Length, MaxTextLength)] : null;
        if (value.Number is null && text is null)
        {
            Tombstone(c, existing);
            return;
        }

        if (existing.Count == 0)
        {
            c.Insert(Stamp(new MetricEntryEntity
            {
                EnrollmentId = target.EnrollmentId,
                DayIndex = target.DayIndex,
                SessionIndex = target.SessionIndex,
                BonusWorkoutId = target.BonusWorkoutId,
                SubjectKey = target.SubjectKey,
                WorkoutId = target.WorkoutId,
                MetricId = metricId,
                Value = value.Number,
                TextValue = text,
            }));
            return;
        }

        var keep = existing[0];
        if (keep.Value != value.Number || keep.TextValue != text)
        {
            keep.Value = value.Number;
            keep.TextValue = text;
            Touch(c, keep);
        }

        Tombstone(c, existing.Skip(1));
    }

    private static List<MetricEntryEntity> Newest(IEnumerable<MetricEntryEntity> rows) =>
        [
            .. rows
                .GroupBy(m => (m.SubjectKey, m.WorkoutId, m.MetricId))
                .Select(g => g.OrderByDescending(m => m.UpdatedAt).First())
                .OrderBy(m => m.DayIndex ?? -1)
                .ThenBy(m => m.SessionIndex ?? -1),
        ];
}
