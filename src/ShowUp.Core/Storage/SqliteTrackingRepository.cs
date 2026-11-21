using System.Linq.Expressions;
using ShowUp.Core.Tracking;
using SQLite;

namespace ShowUp.Core.Storage;

public sealed class SqliteTrackingRepository(
    ShowUpDatabase db,
    TimeProvider time,
    IUserScope user,
    IDeviceIdProvider device) : ITrackingRepository
{
    private DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    public async Task<EnrollmentEntity> EnrollAsync(string programId, int revision, string trackId, DateOnly startDate)
    {
        var connection = await db.GetConnectionAsync();
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
        var connection = await db.GetConnectionAsync();
        var rows = await QueryAsync<EnrollmentEntity>(connection, e => e.DeletedAt == null && e.EndedAt == null);
        return rows.OrderBy(e => e.CreatedAt).ToList();
    }

    public async Task EndEnrollmentAsync(Guid enrollmentId, EnrollmentEndReason reason)
    {
        var connection = await db.GetConnectionAsync();
        var row = await connection.FindAsync<EnrollmentEntity>(enrollmentId);
        if (row is null || !IsVisible(row))
        {
            return;
        }

        DateTimeOffset now = time.GetUtcNow();
        row.EndedAt = now;
        row.EndReason = reason;
        row.UpdatedAt = now;
        await connection.UpdateAsync(row);
    }

    public async Task CrossSessionAsync(Guid enrollmentId, int dayIndex, int sessionIndex, DateOnly dayDate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dayIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(sessionIndex);

        DateOnly today = Today;
        if (!CrossingPolicy.CanCross(dayDate, today))
        {
            throw new InvalidOperationException($"Cannot cross a future day ({dayDate}).");
        }

        var connection = await db.GetConnectionAsync();
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
            CompletedAt = time.GetUtcNow(),
        });
        await connection.InsertAsync(entity);
    }

    public async Task UncrossSessionAsync(Guid enrollmentId, int dayIndex, int sessionIndex)
    {
        var connection = await db.GetConnectionAsync();
        await RequireActiveEnrollmentAsync(connection, enrollmentId);
        var rows = await MatchingCompletionsAsync(connection, enrollmentId, dayIndex, sessionIndex);
        DateTimeOffset now = time.GetUtcNow();
        foreach (CompletionEntity row in rows)
        {
            row.DeletedAt = now;
            row.UpdatedAt = now;
            await connection.UpdateAsync(row);
        }
    }

    public async Task<IReadOnlySet<(int Day, int Session)>> GetCompletedSessionsAsync(Guid enrollmentId)
    {
        var completions = await GetCompletionsAsync(enrollmentId);
        return completions.Select(c => (c.DayIndex, c.SessionIndex)).ToHashSet();
    }

    public async Task<IReadOnlyList<CompletedSession>> GetCompletionsAsync(Guid enrollmentId)
    {
        var connection = await db.GetConnectionAsync();
        var rows = await QueryAsync<CompletionEntity>(connection, c => c.EnrollmentId == enrollmentId && c.DeletedAt == null);
        return rows
            .GroupBy(c => (c.DayIndex, c.SessionIndex))
            .Select(g => g.OrderBy(c => c.CompletedAt).ThenBy(c => c.Id.ToString("D"), StringComparer.Ordinal).First())
            .OrderBy(c => c.DayIndex)
            .ThenBy(c => c.SessionIndex)
            .Select(c => new CompletedSession(c.DayIndex, c.SessionIndex, c.Id, c.CompletedOn))
            .ToList();
    }

    public async Task<IReadOnlyList<ScheduleShift>> GetShiftsAsync(Guid enrollmentId)
    {
        var connection = await db.GetConnectionAsync();
        var rows = await QueryAsync<ScheduleShiftEntity>(connection, s => s.EnrollmentId == enrollmentId && s.DeletedAt == null);
        return rows
            .OrderBy(s => s.CreatedAt)
            .ThenBy(s => s.Id.ToString("D"), StringComparer.Ordinal)
            .Select(s => new ScheduleShift(s.FromDate, s.Days))
            .ToList();
    }

    public async Task ShiftScheduleAsync(Guid enrollmentId, DateOnly fromDate, int days)
    {
        var connection = await db.GetConnectionAsync();
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
        var connection = await db.GetConnectionAsync();
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
        var connection = await db.GetConnectionAsync();
        int fromNumber = from.DayNumber;
        int toNumber = to.DayNumber;
        var rows = await QueryAsync<BonusWorkoutEntity>(
            connection,
            b => b.DeletedAt == null && b.DayNumber >= fromNumber && b.DayNumber <= toNumber);
        return rows.OrderBy(b => b.DayNumber).ThenBy(b => b.CreatedAt).ToList();
    }

    public async Task RemoveBonusAsync(Guid id)
    {
        var connection = await db.GetConnectionAsync();
        var row = await connection.FindAsync<BonusWorkoutEntity>(id);
        if (row is null || !IsVisible(row))
        {
            return;
        }

        DateTimeOffset now = time.GetUtcNow();
        row.DeletedAt = now;
        row.UpdatedAt = now;
        await connection.UpdateAsync(row);
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

    private T Stamp<T>(T entity)
        where T : SyncableEntity
    {
        DateTimeOffset now = time.GetUtcNow();
        entity.Id = Guid.NewGuid();
        entity.UserId = user.CurrentUserId;
        entity.DeviceId = device.DeviceId;
        entity.CreatedAt = now;
        entity.UpdatedAt = now;
        return entity;
    }

    private bool IsVisible(SyncableEntity e) => e.DeletedAt is null && e.UserId == user.CurrentUserId;

    private Task<List<T>> QueryAsync<T>(SQLiteAsyncConnection connection, Expression<Func<T, bool>> where)
        where T : SyncableEntity, new()
    {
        string? uid = user.CurrentUserId;
        return connection.Table<T>().Where(where).Where(x => x.UserId == uid).ToListAsync();
    }

    private Task<List<CompletionEntity>> MatchingCompletionsAsync(
        SQLiteAsyncConnection connection, Guid enrollmentId, int dayIndex, int sessionIndex) =>
        QueryAsync<CompletionEntity>(
            connection,
            c => c.EnrollmentId == enrollmentId && c.DayIndex == dayIndex && c.SessionIndex == sessionIndex && c.DeletedAt == null);
}
