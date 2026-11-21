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

        await notes.SetNoteAsync(_enrollment, null, null, "  Going all in  ");
        await notes.SetNoteAsync(_enrollment, 3, null, "Legs sore");
        await notes.SetNoteAsync(_enrollment, 3, 1, "Skipped the last round");

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
        await notes.SetNoteAsync(_enrollment, 0, null, "first");
        var id = (await notes.GetNotesAsync(_enrollment)).Single().Id;
        Time.Advance(TimeSpan.FromMinutes(1));

        await notes.SetNoteAsync(_enrollment, 0, null, "second");

        var note = (await notes.GetNotesAsync(_enrollment)).Should().ContainSingle().Subject;
        note.Id.Should().Be(id);
        note.Text.Should().Be("second");
        note.UpdatedAt.Should().Be(Time.GetUtcNow());
    }

    [TestMethod]
    public async Task Notes_Blank_LeavesATombstone()
    {
        var notes = CreateNotesRepository();
        await notes.SetNoteAsync(_enrollment, 0, null, "note");

        await notes.SetNoteAsync(_enrollment, 0, null, "   ");

        (await notes.GetNotesAsync(_enrollment)).Should().BeEmpty();
        var connection = await Db.GetConnectionAsync();
        (await connection.Table<NoteEntity>().ToListAsync()).Should().ContainSingle().Which.DeletedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Notes_OtherUser_AreNotVisible()
    {
        var notes = CreateNotesRepository();
        await notes.SetNoteAsync(_enrollment, null, null, "mine");

        User.CurrentUserId = "someone-else";

        (await notes.GetNotesAsync(_enrollment)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task Metrics_Upsert_ReplacesValue()
    {
        var metrics = CreateMetricsRepository();
        var target = MetricTarget.ForSession(_enrollment, 0, 0, "fit-test");

        await metrics.SetValueAsync(target, "jacks", new MetricValue(40));
        await metrics.SetValueAsync(target, "jacks", new MetricValue(52));

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
        await metrics.SetValueAsync(target, "jacks", new MetricValue(40));

        await metrics.SetValueAsync(target, "jacks", new MetricValue(null));

        (await metrics.GetSessionEntriesAsync(_enrollment)).Should().BeEmpty();
        var connection = await Db.GetConnectionAsync();
        (await connection.Table<MetricEntryEntity>().ToListAsync()).Should().ContainSingle().Which.DeletedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Metrics_BonusAndSession_AreKeptApart()
    {
        var metrics = CreateMetricsRepository();
        var bonusId = Guid.NewGuid();
        await metrics.SetValueAsync(MetricTarget.ForSession(_enrollment, 2, 0, "w"), "m", new MetricValue(1));
        await metrics.SetValueAsync(MetricTarget.ForSession(_enrollment, 2, 1, "w"), "m", new MetricValue(2));
        await metrics.SetValueAsync(MetricTarget.ForBonus(bonusId, "w"), "m", new MetricValue(3));
        await metrics.SetValueAsync(MetricTarget.ForBonus(bonusId, "w"), "note", new MetricValue(null, " felt good "));

        (await metrics.GetSessionEntriesAsync(_enrollment)).Select(e => e.Value).Should().Equal(1, 2);
        var bonus = await metrics.GetBonusEntriesAsync([bonusId]);
        bonus.Select(e => (e.MetricId, e.Value, e.TextValue)).Should().BeEquivalentTo([("m", (double?)3, (string?)null), ("note", null, "felt good")]);
        (await metrics.GetBonusEntriesAsync([Guid.NewGuid()])).Should().BeEmpty();
    }

    [TestMethod]
    public async Task Metrics_OtherUser_AreNotVisible()
    {
        var metrics = CreateMetricsRepository();
        await metrics.SetValueAsync(MetricTarget.ForSession(_enrollment, 0, 0, "w"), "m", new MetricValue(1));

        User.CurrentUserId = "someone-else";

        (await metrics.GetSessionEntriesAsync(_enrollment)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task Metrics_NotANumber_Throws()
    {
        var metrics = CreateMetricsRepository();

        Func<Task> act = () => metrics.SetValueAsync(MetricTarget.ForSession(_enrollment, 0, 0, "w"), "m", new MetricValue(double.NaN));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
