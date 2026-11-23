using System.Globalization;
using AwesomeAssertions;
using Microsoft.Extensions.Localization;
using ShowUp.Core.Marking;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Marking;

[TestClass]
public class CrossMomentCaptionTests
{
    [TestMethod]
    public void Create_HalfDoneDay_FormatsDayStreakAndProgress()
    {
        var caption = CrossMomentCaption.Create(Info(sessionsDone: 1, streak: 8), new FormattingLocalizer());

        caption.Day.Should().Be("Day 10 of 60");
        caption.Streak.Should().Be("8-day streak");
        caption.Progress.Should().Be("1 of 2 done");
    }

    [TestMethod]
    public void Create_CompletedDay_UsesDayCompleteText()
    {
        var caption = CrossMomentCaption.Create(Info(sessionsDone: 2, streak: 9), new FormattingLocalizer());

        caption.Progress.Should().Be("Crossed off.");
    }

    [TestMethod]
    public void Create_NoStreak_HasNoStreakSegment()
    {
        var caption = CrossMomentCaption.Create(Info(sessionsDone: 2, streak: 0), new FormattingLocalizer());

        caption.Streak.Should().BeNull();
    }

    [TestMethod]
    public void Announcement_WithStreak_IsOnePlainSentence()
    {
        var caption = CrossMomentCaption.Create(Info(sessionsDone: 1, streak: 9), new FormattingLocalizer());

        caption.Announcement.Should().Be("Day 10 of 60, 9-day streak. 1 of 2 done");
    }

    [TestMethod]
    public void Announcement_NoStreak_SkipsStreak()
    {
        var caption = CrossMomentCaption.Create(Info(sessionsDone: 2, streak: 0), new FormattingLocalizer());

        caption.Announcement.Should().Be("Day 10 of 60. Crossed off.");
    }

    [TestMethod]
    public void Create_NoMilestone_HasNoMilestoneLine()
    {
        var caption = CrossMomentCaption.Create(Info(sessionsDone: 2, streak: 9), new FormattingLocalizer());

        caption.Milestone.Should().BeNull();
        caption.Announcement.Should().Be("Day 10 of 60, 9-day streak. Crossed off.");
    }

    [TestMethod]
    public void Create_WeekMilestone_NamesTheWeek()
    {
        var info = Info(sessionsDone: 2, streak: 9) with { Milestone = new Milestone(MilestoneKind.Week, "Month 1", 3) };

        CrossMomentCaption.Create(info, new FormattingLocalizer()).Milestone.Should().Be("Week 3 done");
    }

    [TestMethod]
    public void Create_PhaseMilestone_NamesThePhase()
    {
        var info = Info(sessionsDone: 2, streak: 9) with { Milestone = new Milestone(MilestoneKind.Phase, "Month 1", 4) };

        CrossMomentCaption.Create(info, new FormattingLocalizer()).Milestone.Should().Be("Month 1 done");
    }

    [TestMethod]
    public void Create_ProgramMilestone_IsAnnouncedFirst()
    {
        var info = Info(sessionsDone: 2, streak: 9) with { Milestone = new Milestone(MilestoneKind.Program, "Month 2", 9) };

        var caption = CrossMomentCaption.Create(info, new FormattingLocalizer());

        caption.Milestone.Should().Be("Program finished. You showed up.");
        caption.Announcement.Should().Be("Program finished. You showed up. Day 10 of 60, 9-day streak. Crossed off.");
    }

    private static CrossMomentInfo Info(int sessionsDone, int streak) =>
        new(DayNumber: 10, TotalDays: 60, Title: "Cardio + Core", StrokesBefore: 0, StrokesAfter: 2, Seed: 1,
            SessionsDone: sessionsDone, SessionsRequired: 2, CurrentStreak: streak);

    private sealed class FormattingLocalizer : IStringLocalizer
    {
        private static readonly Dictionary<string, string> _strings = new()
        {
            ["CrossMomentDayFormat"] = "Day {0} of {1}",
            ["CrossMomentStreakFormat"] = "{0}-day streak",
            ["CrossMomentProgressFormat"] = "{0} of {1} done",
            ["CrossMomentDayComplete"] = "Crossed off.",
            ["CrossMomentMilestoneWeek"] = "Week {0} done",
            ["CrossMomentMilestonePhase"] = "{0} done",
            ["CrossMomentMilestoneProgram"] = "Program finished. You showed up.",
        };

        public LocalizedString this[string name] => new(name, _strings[name]);

        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(CultureInfo.InvariantCulture, _strings[name], arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
