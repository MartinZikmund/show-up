using System.Globalization;
using AwesomeAssertions;
using Microsoft.Extensions.Localization;
using ShowUp.Core.Marking;

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
        };

        public LocalizedString this[string name] => new(name, _strings[name]);

        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(CultureInfo.InvariantCulture, _strings[name], arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
