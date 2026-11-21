using AwesomeAssertions;
using ShowUp.Core.Storage;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Storage;

[TestClass]
public class JournalRepositoriesTests : StorageTestBase
{
    private static readonly Guid _enrollment = Guid.NewGuid();

    [TestMethod]
    public async Task Notes_ProgramAndDay_RoundTrip()
    {
        var notes = CreateNotesRepository();

        await notes.SetNotesAsync(_enrollment, [new NoteEdit(null, null, "  Going all in  "), new NoteEdit(3, null, "Legs sore"), new NoteEdit(3, 1, "Skipped the last round")]);

        var all = await notes.GetNotesAsync(_enrollment);

        all.Select(n => (n.DayIndex, n.SessionIndex, n.Text)).Should().Equal(
            (null, null, "Going all in"),
            (3, null, "Legs sore"),
            (3, 1, "Skipped the last round"));
        all.Should().OnlyContain(n => n.DeviceId == "device-1" && n.CreatedAt == Time.GetUtcNow());
    }

    [TestMethod]
    public async Task Notes_SetAgain_UpdatesInPlace()
    {
        var notes = CreateNotesRepository();
        await notes.SetNotesAsync(_enrollment, [new NoteEdit(0, null, "first")]);
        var id = (await notes.GetNotesAsync(_enrollment)).Single().Id;
        Time.Advance(TimeSpan.FromMinutes(1));

        await notes.SetNotesAsync(_enrollment, [new NoteEdit(0, null, "second")]);

        var note = (await notes.GetNotesAsync(_enrollment)).Should().ContainSingle().Subject;
        note.Id.Should().Be(id);
        note.Text.Should().Be("second");
        note.UpdatedAt.Should().Be(Time.GetUtcNow());
    }

    [TestMethod]
    public async Task Notes_Blank_LeavesATombstone()
    {
        var notes = CreateNotesRepository();
        await notes.SetNotesAsync(_enrollment, [new NoteEdit(0, null, "note")]);

        await notes.SetNotesAsync(_enrollment, [new NoteEdit(0, null, "   ")]);

        (await notes.GetNotesAsync(_enrollment)).Should().BeEmpty();
        var connection = await Db.GetConnectionAsync();
        (await connection.Table<NoteEntity>().ToListAsync()).Should().ContainSingle().Which.DeletedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Notes_OtherUser_AreNotVisible()
    {
        var notes = CreateNotesRepository();
        await notes.SetNotesAsync(_enrollment, [new NoteEdit(null, null, "mine")]);

        User.CurrentUserId = "someone-else";

        (await notes.GetNotesAsync(_enrollment)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task Metrics_Upsert_ReplacesValue()
    {
        var metrics = CreateMetricsRepository();
        var target = MetricTarget.ForSession(_enrollment, 0, 0, "fit-test");

        await metrics.SetValuesAsync(target, new Dictionary<string, MetricValue> { ["jacks"] = new MetricValue(40) });
        await metrics.SetValuesAsync(target, new Dictionary<string, MetricValue> { ["jacks"] = new MetricValue(52) });

        var entry = (await metrics.GetSessionEntriesAsync(_enrollment)).Should().ContainSingle().Subject;
        entry.Value.Should().Be(52);
        entry.WorkoutId.Should().Be("fit-test");
        var connection = await Db.GetConnectionAsync();
        (await connection.Table<MetricEntryEntity>().CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task Metrics_Empty_LeavesATombstone()
    {
        var metrics = CreateMetricsRepository();
        var target = MetricTarget.ForSession(_enrollment, 0, 0, "fit-test");
        await metrics.SetValuesAsync(target, new Dictionary<string, MetricValue> { ["jacks"] = new MetricValue(40) });

        await metrics.SetValuesAsync(target, new Dictionary<string, MetricValue> { ["jacks"] = new MetricValue(null) });

        (await metrics.GetSessionEntriesAsync(_enrollment)).Should().BeEmpty();
        var connection = await Db.GetConnectionAsync();
        (await connection.Table<MetricEntryEntity>().ToListAsync()).Should().ContainSingle().Which.DeletedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Metrics_BonusAndSession_AreKeptApart()
    {
        var metrics = CreateMetricsRepository();
        var bonusId = Guid.NewGuid();
        await metrics.SetValuesAsync(MetricTarget.ForSession(_enrollment, 2, 0, "w"), new Dictionary<string, MetricValue> { ["m"] = new MetricValue(1) });
        await metrics.SetValuesAsync(MetricTarget.ForSession(_enrollment, 2, 1, "w"), new Dictionary<string, MetricValue> { ["m"] = new MetricValue(2) });
        await metrics.SetValuesAsync(MetricTarget.ForBonus(bonusId, "w"), new Dictionary<string, MetricValue> { ["m"] = new MetricValue(3) });
        await metrics.SetValuesAsync(MetricTarget.ForBonus(bonusId, "w"), new Dictionary<string, MetricValue> { ["note"] = new MetricValue(null, " felt good ") });

        (await metrics.GetSessionEntriesAsync(_enrollment)).Select(e => e.Value).Should().Equal(1, 2);
        var bonus = await metrics.GetBonusEntriesAsync([bonusId]);
        bonus.Select(e => (e.MetricId, e.Value, e.TextValue)).Should().BeEquivalentTo([("m", (double?)3, (string?)null), ("note", null, "felt good")]);
        (await metrics.GetBonusEntriesAsync([Guid.NewGuid()])).Should().BeEmpty();
    }

    [TestMethod]
    public async Task RemoveBonus_TombstonesItsResults()
    {
        var tracking = CreateRepository();
        var metrics = CreateMetricsRepository();
        var bonus = await tracking.LogBonusAsync("w", "Run", new DateOnly(2026, 12, 10), null);
        var other = await tracking.LogBonusAsync("w", "Swim", new DateOnly(2026, 12, 10), null);
        await metrics.SetValuesAsync(MetricTarget.ForBonus(bonus.Id, "w"), new Dictionary<string, MetricValue> { ["m"] = new(1) });
        await metrics.SetValuesAsync(MetricTarget.ForBonus(other.Id, "w"), new Dictionary<string, MetricValue> { ["m"] = new(2) });
        Device.DeviceId = "dev-y";
        Time.Advance(TimeSpan.FromMinutes(5));

        await tracking.RemoveBonusAsync(bonus.Id);

        (await metrics.GetBonusEntriesAsync([bonus.Id, other.Id])).Select(e => e.BonusWorkoutId).Should().Equal(other.Id);
        var connection = await Db.GetConnectionAsync();
        var gone = (await connection.Table<MetricEntryEntity>().ToListAsync()).Single(m => m.BonusWorkoutId == bonus.Id);
        gone.DeletedAt.Should().Be(Time.GetUtcNow());
        gone.UpdatedAt.Should().Be(Time.GetUtcNow());
        gone.DeviceId.Should().Be("dev-y");
    }

    [TestMethod]
    public async Task Notes_TwoDevicesSameKey_ReadsTheNewest()
    {
        var key = SubjectKeys.Note(_enrollment, 2, null);
        var connection = await Db.GetConnectionAsync();
        await connection.InsertAsync(Note(key, "from the phone", "phone", Time.GetUtcNow()));
        await connection.InsertAsync(Note(key, "from the laptop", "laptop", Time.GetUtcNow().AddMinutes(3)));

        var notes = await CreateNotesRepository().GetNotesAsync(_enrollment);

        notes.Should().ContainSingle().Which.Text.Should().Be("from the laptop");
    }

    [TestMethod]
    public async Task Metrics_TwoDevicesSameKey_ReadsTheNewest()
    {
        var target = MetricTarget.ForSession(_enrollment, 1, 0, "w");
        var connection = await Db.GetConnectionAsync();
        await connection.InsertAsync(Metric(target.SubjectKey, 10, "phone", Time.GetUtcNow().AddMinutes(3)));
        await connection.InsertAsync(Metric(target.SubjectKey, 20, "laptop", Time.GetUtcNow()));

        var entries = await CreateMetricsRepository().GetSessionEntriesAsync(_enrollment);

        entries.Should().ContainSingle().Which.Value.Should().Be(10);
    }

    [TestMethod]
    public async Task Metrics_TwoDevicesSameKey_NextSaveCollapsesThem()
    {
        var target = MetricTarget.ForSession(_enrollment, 1, 0, "w");
        var connection = await Db.GetConnectionAsync();
        await connection.InsertAsync(Metric(target.SubjectKey, 10, "phone", Time.GetUtcNow()));
        await connection.InsertAsync(Metric(target.SubjectKey, 20, "laptop", Time.GetUtcNow().AddMinutes(1)));

        await CreateMetricsRepository().SetValuesAsync(target, new Dictionary<string, MetricValue> { ["m"] = new(30) });

        (await connection.Table<MetricEntryEntity>().ToListAsync()).Count(m => m.DeletedAt is null).Should().Be(1);
        (await CreateMetricsRepository().GetSessionEntriesAsync(_enrollment)).Single().Value.Should().Be(30);
    }

    private NoteEntity Note(string key, string text, string device, DateTimeOffset updatedAt) => new()
    {
        Id = Guid.NewGuid(),
        EnrollmentId = _enrollment,
        DayIndex = 2,
        SubjectKey = key,
        Text = text,
        DeviceId = device,
        CreatedAt = Time.GetUtcNow(),
        UpdatedAt = updatedAt,
    };

    private MetricEntryEntity Metric(string key, double value, string device, DateTimeOffset updatedAt) => new()
    {
        Id = Guid.NewGuid(),
        EnrollmentId = _enrollment,
        DayIndex = 1,
        SessionIndex = 0,
        SubjectKey = key,
        WorkoutId = "w",
        MetricId = "m",
        Value = value,
        DeviceId = device,
        CreatedAt = Time.GetUtcNow(),
        UpdatedAt = updatedAt,
    };

    [TestMethod]
    public async Task Metrics_OtherUser_AreNotVisible()
    {
        var metrics = CreateMetricsRepository();
        await metrics.SetValuesAsync(MetricTarget.ForSession(_enrollment, 0, 0, "w"), new Dictionary<string, MetricValue> { ["m"] = new MetricValue(1) });

        User.CurrentUserId = "someone-else";

        (await metrics.GetSessionEntriesAsync(_enrollment)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task Metrics_NotANumber_Throws()
    {
        var metrics = CreateMetricsRepository();

        Func<Task> act = () => metrics.SetValuesAsync(MetricTarget.ForSession(_enrollment, 0, 0, "w"), new Dictionary<string, MetricValue> { ["m"] = new MetricValue(double.NaN) });

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public async Task SubjectKeys_NotesAndResults_AreStoredForUpserts()
    {
        var bonusId = Guid.NewGuid();
        await CreateNotesRepository().SetNotesAsync(_enrollment, [new NoteEdit(null, null, "p"), new NoteEdit(2, null, "d"), new NoteEdit(2, 1, "s")]);
        await CreateMetricsRepository().SetValuesAsync(MetricTarget.ForSession(_enrollment, 2, 1, "w"), new Dictionary<string, MetricValue> { ["m"] = new(1) });
        await CreateMetricsRepository().SetValuesAsync(MetricTarget.ForBonus(bonusId, "w"), new Dictionary<string, MetricValue> { ["m"] = new(2) });

        var connection = await Db.GetConnectionAsync();
        var noteKeys = (await connection.Table<NoteEntity>().ToListAsync()).Select(n => n.SubjectKey);
        var metricKeys = (await connection.Table<MetricEntryEntity>().ToListAsync()).Select(m => m.SubjectKey);

        noteKeys.Should().BeEquivalentTo($"p:{_enrollment:D}", $"d:{_enrollment:D}:2", $"s:{_enrollment:D}:2:1");
        metricKeys.Should().BeEquivalentTo($"s:{_enrollment:D}:2:1", $"b:{bonusId:D}");
    }

    [TestMethod]
    public async Task Metrics_OneInvalidValue_WritesNothing()
    {
        var metrics = CreateMetricsRepository();

        Func<Task> act = () => metrics.SetValuesAsync(
            MetricTarget.ForSession(_enrollment, 0, 0, "w"),
            new Dictionary<string, MetricValue> { ["a"] = new(1), ["b"] = new(double.PositiveInfinity) });

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await metrics.GetSessionEntriesAsync(_enrollment)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task Metrics_SameSubjectOtherWorkout_IsNotOverwritten()
    {
        var metrics = CreateMetricsRepository();
        await metrics.SetValuesAsync(MetricTarget.ForSession(_enrollment, 0, 0, "old"), new Dictionary<string, MetricValue> { ["m"] = new(1) });

        await metrics.SetValuesAsync(MetricTarget.ForSession(_enrollment, 0, 0, "new"), new Dictionary<string, MetricValue> { ["m"] = new(2) });

        (await metrics.GetSessionEntriesAsync(_enrollment)).Select(e => (e.WorkoutId, e.Value)).Should().BeEquivalentTo([("old", (double?)1), ("new", (double?)2)]);
    }

    [TestMethod]
    public void MetricTarget_Shapes_AreExclusive()
    {
        var bonus = MetricTarget.ForBonus(Guid.NewGuid(), "w");
        var session = MetricTarget.ForSession(_enrollment, 1, 0, "w");

        bonus.Should().BeEquivalentTo(new { IsBonus = true, EnrollmentId = (Guid?)null, DayIndex = (int?)null, SessionIndex = (int?)null });
        session.BonusWorkoutId.Should().BeNull();
        FluentActions.Invoking(() => MetricTarget.ForSession(_enrollment, -1, 0, "w")).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => MetricTarget.ForBonus(Guid.NewGuid(), " ")).Should().Throw<ArgumentException>();
    }
}
