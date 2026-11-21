using ShowUp.Core.Tracking;
using SQLite;

namespace ShowUp.Core.Storage;

public sealed class SqliteTrackingRepository(
    ShowUpDatabase db,
    TimeProvider time,
    IUserScope user,
    IDeviceIdProvider device) : SqliteRepositoryBase(db, time, user, device), ITrackingRepository
{
    private const int MaxShiftDays = 30;

    public async Task<EnrollmentEntity> EnrollAsync(string programId, int revision, string trackId, DateOnly startDate)
    {
        var connection = await ConnectAsync();
        var entity = Stamp(new EnrollmentEntity
        {
            ProgramId = programId,
            ProgramRevision = revision,
            TrackId = trackId,
            StartDate = startDate,
        });
        await connection.InsertAsync(entity);
        return entity;
    }

    public async Task<IReadOnlyList<EnrollmentEntity>> GetActiveEnrollmentsAsync()
    {
        var connection = await ConnectAsync();
        var rows = await QueryAsync<EnrollmentEntity>(connection, e => e.DeletedAt == null && e.EndedAt == null);
        return rows.OrderBy(e => e.CreatedAt).ToList();
    }

    public async Task EndEnrollmentAsync(Guid enrollmentId, EnrollmentEndReason reason)
    {
        var connection = await ConnectAsync();
        var row = await connection.FindAsync<EnrollmentEntity>(enrollmentId);
        if (row is null || !IsVisible(row))
        {
            return;
        }

        row.EndedAt = UtcNow;
        row.EndReason = reason;
        await connection.RunInTransactionAsync(c => Touch(c, row));
    }

    public async Task CrossSessionAsync(Guid enrollmentId, int dayIndex, int sessionIndex, DateOnly dayDate, string? inkJson = null, Guid? completionId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dayIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(sessionIndex);

        DateOnly today = Today;
        if (!CrossingPolicy.CanCross(dayDate, today))
        {
            throw new InvalidOperationException($"Cannot cross a future day ({dayDate}).");
        }

        var connection = await ConnectAsync();
        await RequireActiveEnrollmentAsync(connection, enrollmentId);
        var existing = await MatchingCompletionsAsync(connection, enrollmentId, dayIndex, sessionIndex);
        if (existing.Count > 0)
        {
            return;
        }

        var entity = Stamp(new CompletionEntity
        {
            EnrollmentId = enrollmentId,
            DayIndex = dayIndex,
            SessionIndex = sessionIndex,
            CompletedOn = today,
            CompletedAt = UtcNow,
            InkJson = inkJson,
        });
        entity.Id = completionId ?? entity.Id;
        await connection.InsertAsync(entity);
    }

    public async Task UncrossSessionAsync(Guid enrollmentId, int dayIndex, int sessionIndex)
    {
        var connection = await ConnectAsync();
        await RequireActiveEnrollmentAsync(connection, enrollmentId);
        var rows = await MatchingCompletionsAsync(connection, enrollmentId, dayIndex, sessionIndex);
        await connection.RunInTransactionAsync(c => Tombstone(c, rows));
    }

    public async Task<IReadOnlySet<(int Day, int Session)>> GetCompletedSessionsAsync(Guid enrollmentId)
    {
        var completions = await GetCompletionsAsync(enrollmentId);
        return completions.Select(c => (c.DayIndex, c.SessionIndex)).ToHashSet();
    }

    public async Task<IReadOnlyList<CompletedSession>> GetCompletionsAsync(Guid enrollmentId)
    {
        var connection = await ConnectAsync();
        var rows = await QueryAsync<CompletionEntity>(connection, c => c.EnrollmentId == enrollmentId && c.DeletedAt == null);
        return rows
            .GroupBy(c => (c.DayIndex, c.SessionIndex))
            .Select(g => g.OrderBy(c => c.CompletedAt).ThenBy(c => c.Id.ToString("D"), StringComparer.Ordinal).First())
            .OrderBy(c => c.DayIndex)
            .ThenBy(c => c.SessionIndex)
            .Select(c => new CompletedSession(c.DayIndex, c.SessionIndex, c.Id, c.CompletedOn, c.InkJson, c.CompletedAt))
            .ToList();
    }

    public async Task<IReadOnlyList<ScheduleShift>> GetShiftsAsync(Guid enrollmentId)
    {
        var connection = await ConnectAsync();
        var rows = await QueryAsync<ScheduleShiftEntity>(connection, s => s.EnrollmentId == enrollmentId && s.DeletedAt == null);
        return rows
            .OrderBy(s => s.CreatedAt)
            .ThenBy(s => s.Id.ToString("D"), StringComparer.Ordinal)
            .Select(s => new ScheduleShift(s.FromDate, s.Days))
            .ToList();
    }

    public async Task ShiftScheduleAsync(Guid enrollmentId, DateOnly fromDate, int days, int dayCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(days, MaxShiftDays);

        var connection = await ConnectAsync();
        EnrollmentEntity enrollment = await RequireActiveEnrollmentAsync(connection, enrollmentId);

        EnrollmentCalendar calendar = new(enrollment.StartDate, dayCount, await GetShiftsAsync(enrollmentId));
        var completions = await GetCompletionsAsync(enrollmentId);
        int lastCrossed = completions.Where(c => c.DayIndex < dayCount).Select(c => c.DayIndex).DefaultIfEmpty(-1).Max();
        if (lastCrossed >= 0)
        {
            DateOnly earliest = calendar.DateOf(lastCrossed).AddDays(1);
            if (fromDate < earliest)
            {
                fromDate = earliest;
            }
        }

        if (fromDate > calendar.EndDate)
        {
            throw new InvalidOperationException($"Nothing left to shift from {fromDate}; the schedule ends on {calendar.EndDate}.");
        }

        var entity = Stamp(new ScheduleShiftEntity
        {
            EnrollmentId = enrollmentId,
            FromDate = fromDate,
            Days = days,
        });
        await connection.InsertAsync(entity);
    }

    public async Task<BonusWorkoutEntity> LogBonusAsync(string? workoutId, string title, DateOnly date, int? durationMinutes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (!CrossingPolicy.CanCross(date, Today))
        {
            throw new ArgumentOutOfRangeException(nameof(date), date, "A bonus workout can't be logged for a future day.");
        }

        var connection = await ConnectAsync();
        var entity = Stamp(new BonusWorkoutEntity
        {
            WorkoutId = workoutId,
            Title = title,
            Date = date,
            DurationMinutes = durationMinutes,
        });
        await connection.InsertAsync(entity);
        return entity;
    }

    public async Task<IReadOnlyList<BonusWorkoutEntity>> GetBonusWorkoutsAsync(DateOnly from, DateOnly to)
    {
        var connection = await ConnectAsync();
        int fromNumber = from.DayNumber;
        int toNumber = to.DayNumber;
        var rows = await QueryAsync<BonusWorkoutEntity>(
            connection,
            b => b.DeletedAt == null && b.DayNumber >= fromNumber && b.DayNumber <= toNumber);
        return rows.OrderBy(b => b.DayNumber).ThenBy(b => b.CreatedAt).ToList();
    }

    public async Task RemoveBonusAsync(Guid id)
    {
        var connection = await ConnectAsync();
        var row = await connection.FindAsync<BonusWorkoutEntity>(id);
        if (row is null || !IsVisible(row))
        {
            return;
        }

        await connection.RunInTransactionAsync(c =>
        {
            Touch(c, row, delete: true);
            Tombstone(c, Query<MetricEntryEntity>(c, m => m.BonusWorkoutId == id));
        });
    }

    private async Task<EnrollmentEntity> RequireActiveEnrollmentAsync(SQLiteAsyncConnection connection, Guid enrollmentId)
    {
        var row = await connection.FindAsync<EnrollmentEntity>(enrollmentId);
        if (row is null || !IsVisible(row) || row.EndedAt is not null)
        {
            throw new InvalidOperationException($"Enrollment {enrollmentId} does not exist or is not active.");
        }

        return row;
    }

    private Task<List<CompletionEntity>> MatchingCompletionsAsync(
        SQLiteAsyncConnection connection, Guid enrollmentId, int dayIndex, int sessionIndex) =>
        QueryAsync<CompletionEntity>(
            connection,
            c => c.EnrollmentId == enrollmentId && c.DayIndex == dayIndex && c.SessionIndex == sessionIndex && c.DeletedAt == null);
}
