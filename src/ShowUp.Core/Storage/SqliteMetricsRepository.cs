using ShowUp.Core.Tracking;
using SQLite;

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

    public async Task SetValueAsync(MetricTarget target, string metricId, MetricValue value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(metricId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.WorkoutId);
        if (!target.IsBonus && (target.EnrollmentId is null || target.DayIndex is null || target.SessionIndex is null))
        {
            throw new ArgumentException("A session result needs its enrollment, day and session.", nameof(target));
        }

        if (value.Number is { } number && !double.IsFinite(number))
        {
            throw new ArgumentOutOfRangeException(nameof(value), number, "A result must be a finite number.");
        }

        var text = value.Text?.Trim() is { Length: > 0 } t ? t[..Math.Min(t.Length, MaxTextLength)] : null;
        var connection = await ConnectAsync();
        var existing = await MatchingAsync(connection, target, metricId);

        if (value.Number is null && text is null)
        {
            await TombstoneDuplicatesAsync(connection, existing);
            return;
        }

        if (existing.Count == 0)
        {
            await connection.InsertAsync(Stamp(new MetricEntryEntity
            {
                EnrollmentId = target.EnrollmentId,
                DayIndex = target.DayIndex,
                SessionIndex = target.SessionIndex,
                BonusWorkoutId = target.BonusWorkoutId,
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
            await TouchAsync(connection, keep);
        }

        await TombstoneDuplicatesAsync(connection, existing.Skip(1));
    }

    private async Task<List<MetricEntryEntity>> MatchingAsync(SQLiteAsyncConnection connection, MetricTarget target, string metricId)
    {
        Guid? enrollmentId = target.EnrollmentId;
        Guid? bonusId = target.BonusWorkoutId;
        var rows = target.IsBonus
            ? await QueryAsync<MetricEntryEntity>(connection, m => m.MetricId == metricId && m.BonusWorkoutId == bonusId)
            : await QueryAsync<MetricEntryEntity>(connection, m => m.MetricId == metricId && m.EnrollmentId == enrollmentId);
        return [.. rows.Where(target.Matches).OrderByDescending(m => m.UpdatedAt)];
    }

    private static List<MetricEntryEntity> Newest(IEnumerable<MetricEntryEntity> rows) =>
        [
            .. rows
                .GroupBy(m => (m.EnrollmentId, m.DayIndex, m.SessionIndex, m.BonusWorkoutId, m.MetricId))
                .Select(g => g.OrderByDescending(m => m.UpdatedAt).First())
                .OrderBy(m => m.DayIndex ?? -1)
                .ThenBy(m => m.SessionIndex ?? -1),
        ];
}
