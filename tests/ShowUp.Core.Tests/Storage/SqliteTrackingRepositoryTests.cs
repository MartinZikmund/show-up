using AwesomeAssertions;
using ShowUp.Core.Storage;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Storage;

[TestClass]
public class SqliteTrackingRepositoryTests : StorageTestBase
{
    private static readonly DateOnly _today = new(2026, 12, 10);

    private async Task<(SqliteTrackingRepository Repo, EnrollmentEntity Enrollment)> EnrolledAsync()
    {
        var repo = CreateRepository();
        var enrollment = await repo.EnrollAsync("p", 1, "t", new DateOnly(2026, 12, 1));
        return (repo, enrollment);
    }

    [TestMethod]
    public async Task CrossSession_FutureDay_Throws()
    {
        var (repo, e) = await EnrolledAsync();

        Func<Task> act = () => repo.CrossSessionAsync(e.Id, 0, 0, _today.AddDays(1));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task CrossSession_PastDay_IsAllowed()
    {
        var (repo, e) = await EnrolledAsync();

        await repo.CrossSessionAsync(e.Id, 2, 0, _today.AddDays(-3));

        (await repo.GetCompletedSessionsAsync(e.Id)).Should().BeEquivalentTo([(2, 0)]);
        var connection = await Db.GetConnectionAsync();
        var row = await connection.Table<CompletionEntity>().FirstAsync();
        row.CompletedOnDayNumber.Should().Be(_today.DayNumber);
    }

    [TestMethod]
    public async Task CrossSession_Twice_StoresOneRow()
    {
        var (repo, e) = await EnrolledAsync();

        await repo.CrossSessionAsync(e.Id, 0, 0, _today);
        await repo.CrossSessionAsync(e.Id, 0, 0, _today);

        var connection = await Db.GetConnectionAsync();
        (await connection.Table<CompletionEntity>().CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task GetCompleted_DuplicateRowsFromTwoDevices_CountsOnce()
    {
        var (repo, e) = await EnrolledAsync();
        await InsertCompletionAsync(e.Id, "dev-a");
        await InsertCompletionAsync(e.Id, "dev-b");

        var completed = await repo.GetCompletedSessionsAsync(e.Id);

        completed.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task Uncross_DuplicateRows_TombstonesAll()
    {
        var (repo, e) = await EnrolledAsync();
        await InsertCompletionAsync(e.Id, "dev-a");
        await InsertCompletionAsync(e.Id, "dev-b");

        await repo.UncrossSessionAsync(e.Id, 1, 0);

        (await repo.GetCompletedSessionsAsync(e.Id)).Should().BeEmpty();
        var connection = await Db.GetConnectionAsync();
        var rows = await connection.Table<CompletionEntity>().ToListAsync();
        rows.Should().HaveCount(2).And.OnlyContain(r => r.DeletedAt != null);
    }

    [TestMethod]
    public async Task Reads_OtherUsersRows_AreInvisible()
    {
        User.CurrentUserId = "a";
        var repo = CreateRepository();
        var enrollment = await repo.EnrollAsync("p", 1, "t", _today);
        await repo.LogBonusAsync(null, "Run", _today, 20);

        User.CurrentUserId = "b";
        (await repo.GetActiveEnrollmentsAsync()).Should().BeEmpty();
        (await repo.GetBonusWorkoutsAsync(_today, _today)).Should().BeEmpty();
        (await repo.GetCompletedSessionsAsync(enrollment.Id)).Should().BeEmpty();

        User.CurrentUserId = "a";
        (await repo.GetActiveEnrollmentsAsync()).Should().ContainSingle();
        enrollment.UserId.Should().Be("a");
    }

    [TestMethod]
    public async Task Reads_AnonymousScope_SeesOnlyAnonymousRows()
    {
        User.CurrentUserId = null;
        var repo = CreateRepository();
        await repo.EnrollAsync("p", 1, "t", _today);
        await repo.LogBonusAsync(null, "Run", _today, null);

        (await repo.GetActiveEnrollmentsAsync()).Should().ContainSingle();
        (await repo.GetBonusWorkoutsAsync(_today, _today)).Should().ContainSingle();

        User.CurrentUserId = "a";
        (await repo.GetActiveEnrollmentsAsync()).Should().BeEmpty();
        (await repo.GetBonusWorkoutsAsync(_today, _today)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task EndEnrollment_RemovesFromActive()
    {
        var (repo, e) = await EnrolledAsync();

        await repo.EndEnrollmentAsync(e.Id, EnrollmentEndReason.Finished);

        (await repo.GetActiveEnrollmentsAsync()).Should().BeEmpty();
    }

    [TestMethod]
    public async Task Bonus_LogAndRangeQuery_RoundTrips()
    {
        var repo = CreateRepository();
        await repo.LogBonusAsync("w1", "Pushups", _today, 15);
        var outside = await repo.LogBonusAsync(null, "Old", _today.AddDays(-10), null);

        var inRange = await repo.GetBonusWorkoutsAsync(_today.AddDays(-1), _today.AddDays(1));

        inRange.Should().ContainSingle();
        inRange[0].Title.Should().Be("Pushups");
        inRange[0].WorkoutId.Should().Be("w1");
        inRange[0].Date.Should().Be(_today);
        inRange[0].DurationMinutes.Should().Be(15);

        await repo.RemoveBonusAsync(outside.Id);
        (await repo.GetBonusWorkoutsAsync(_today.AddDays(-20), _today)).Should().ContainSingle();
    }

    [TestMethod]
    public async Task Shifts_ShiftAndRead_RoundTrips()
    {
        var (repo, e) = await EnrolledAsync();

        await repo.ShiftScheduleAsync(e.Id, new DateOnly(2026, 12, 5), 2);

        var shifts = await repo.GetShiftsAsync(e.Id);
        shifts.Should().BeEquivalentTo([new ScheduleShift(new DateOnly(2026, 12, 5), 2)]);
    }

    [TestMethod]
    public async Task GetShifts_SameCreatedAt_OrdersById()
    {
        var (repo, e) = await EnrolledAsync();
        var connection = await Db.GetConnectionAsync();
        Guid low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid high = Guid.Parse("ffffffff-0000-0000-0000-000000000000");
        foreach ((Guid id, int days) in new[] { (high, 5), (low, 3) })
        {
            await connection.InsertAsync(new ScheduleShiftEntity
            {
                Id = id,
                EnrollmentId = e.Id,
                FromDate = new DateOnly(2026, 12, 20),
                Days = days,
                CreatedAt = Time.GetUtcNow(),
                UpdatedAt = Time.GetUtcNow(),
                DeviceId = "d",
            });
        }

        var shifts = await repo.GetShiftsAsync(e.Id);

        shifts.Select(s => s.Days).Should().Equal(3, 5);
    }

    [TestMethod]
    public async Task GetCompletions_DuplicateRows_KeepsEarliestDeterministically()
    {
        var (repo, e) = await EnrolledAsync();
        Guid low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid high = Guid.Parse("ffffffff-0000-0000-0000-000000000000");
        Guid later = Guid.Parse("00000000-0000-0000-0000-000000000000");
        await InsertCompletionAsync(e.Id, "a", high, Time.GetUtcNow());
        await InsertCompletionAsync(e.Id, "b", low, Time.GetUtcNow());
        await InsertCompletionAsync(e.Id, "c", later, Time.GetUtcNow().AddMinutes(5));

        var completions = await repo.GetCompletionsAsync(e.Id);

        completions.Should().ContainSingle().Which.CompletionId.Should().Be(low);
    }

    [TestMethod]
    public async Task GetCompletions_ReturnsCompletedOnDate()
    {
        var (repo, e) = await EnrolledAsync();
        await repo.CrossSessionAsync(e.Id, 3, 1, _today.AddDays(-2));

        var completions = await repo.GetCompletionsAsync(e.Id);

        completions.Should().ContainSingle();
        completions[0].DayIndex.Should().Be(3);
        completions[0].SessionIndex.Should().Be(1);
        completions[0].CompletedOn.Should().Be(_today);
    }

    [TestMethod]
    public async Task Writes_SetUpdatedAtAndDeviceId()
    {
        Device.DeviceId = "dev-x";
        var (repo, e) = await EnrolledAsync();
        e.DeviceId.Should().Be("dev-x");
        e.CreatedAt.Should().Be(Time.GetUtcNow());

        Time.Advance(TimeSpan.FromHours(1));
        await repo.EndEnrollmentAsync(e.Id, EnrollmentEndReason.Abandoned);

        var connection = await Db.GetConnectionAsync();
        var row = await connection.Table<EnrollmentEntity>().FirstAsync();
        row.UpdatedAt.Should().Be(Time.GetUtcNow());
        row.UpdatedAt.Should().BeAfter(row.CreatedAt);
        row.DeviceId.Should().Be("dev-x");
        row.EndReason.Should().Be(EnrollmentEndReason.Abandoned);
        row.EndedAt.Should().Be(Time.GetUtcNow());
    }

    private Task InsertCompletionAsync(Guid enrollmentId, string deviceId) =>
        InsertCompletionAsync(enrollmentId, deviceId, Guid.NewGuid(), Time.GetUtcNow());

    private async Task InsertCompletionAsync(Guid enrollmentId, string deviceId, Guid id, DateTimeOffset completedAt)
    {
        var connection = await Db.GetConnectionAsync();
        await connection.InsertAsync(new CompletionEntity
        {
            Id = id,
            EnrollmentId = enrollmentId,
            DayIndex = 1,
            SessionIndex = 0,
            CompletedOnDayNumber = _today.DayNumber,
            CompletedAt = completedAt,
            CreatedAt = Time.GetUtcNow(),
            UpdatedAt = Time.GetUtcNow(),
            DeviceId = deviceId,
        });
    }
}
