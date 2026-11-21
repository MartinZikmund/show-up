using System.Globalization;
using ShowUp.Core.Marking;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.ViewModels;

public sealed record SessionItem(int Index, string Name, bool IsDone);

/// <summary>One cell of the program calendar. Updated in place on refresh, so a cell only animates what changed.</summary>
public sealed partial class CalendarDayViewModel : ObservableObject
{
    private readonly IStringLocalizer _localizer;

    public CalendarDayViewModel(int dayIndex, IStringLocalizer localizer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dayIndex);
        DayIndex = dayIndex;
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    public int DayIndex { get; }

    public int DayNumber => DayIndex + 1;

    [ObservableProperty]
    public partial DateOnly Date { get; private set; }

    [ObservableProperty]
    public partial string DateLabel { get; private set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<SessionItem> Sessions { get; private set; } = [];

    /// <summary>Session names joined for display ("Pure Cardio + Cardio Abs").</summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    /// <summary>Generated marker strokes (<see cref="MarkerAppearance.StrokesFor"/>); drawn sessions are in <see cref="Inks"/>.</summary>
    [ObservableProperty]
    public partial int MarkStrokes { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> Inks { get; private set; } = [];

    [ObservableProperty]
    public partial bool ShowsTicks { get; private set; }

    [ObservableProperty]
    public partial bool IsToday { get; private set; }

    [ObservableProperty]
    public partial bool IsFuture { get; private set; }

    [ObservableProperty]
    public partial bool IsMissed { get; private set; }

    /// <summary>A rest day, or one whose sessions are all optional (<see cref="DayStatus.CountsAsRest"/>).</summary>
    [ObservableProperty]
    public partial bool IsRestDay { get; private set; }

    /// <summary>Nothing left to cross: a tap offers to uncross instead.</summary>
    [ObservableProperty]
    public partial bool IsComplete { get; private set; }

    [ObservableProperty]
    public partial int BonusCount { get; private set; }

    [ObservableProperty]
    public partial int Seed { get; private set; }

    /// <summary>A session was crossed after the day's date (backfilled).</summary>
    [ObservableProperty]
    public partial bool IsLate { get; private set; }

    [ObservableProperty]
    public partial string AutomationName { get; private set; } = "";

    /// <summary>Set when a locked (future) day is tapped; the cell shakes and clears it.</summary>
    [ObservableProperty]
    public partial bool ShakeRequested { get; set; }

    /// <summary>False while a cross moment shows the cross big, so the cell lands it without animating.</summary>
    public bool AnimateMarks { get; set; } = true;

    internal void RequestShake()
    {
        ShakeRequested = false;
        ShakeRequested = true;
    }

    internal void Update(
        DayStatus status,
        IReadOnlyList<CompletedSession> completions,
        IReadOnlyList<string> sessionNames,
        int bonusCount,
        DateOnly today)
    {
        var autos = completions.Where(c => c.InkJson is null).Select(c => c.SessionIndex).ToHashSet();
        IReadOnlyList<SessionItem> sessions =
            [.. sessionNames.Select((name, i) => new SessionItem(i, name, status.CompletedSessions.Contains(i)))];
        IReadOnlyList<string> inks = [.. completions.Where(c => c.InkJson is not null).Select(c => c.InkJson!)];

        Date = status.Date;
        var dateFormat = _localizer["CalendarDateFormat"];
        DateLabel = status.Date.ToString(dateFormat.ResourceNotFound ? "d" : dateFormat.Value, CultureInfo.CurrentCulture);
        if (!Sessions.SequenceEqual(sessions))
        {
            Sessions = sessions;
        }

        Title = string.Join(" + ", sessionNames);
        IsToday = status.Date == today;
        IsFuture = status.Date > today;
        IsMissed = status.IsMissed(today);
        IsRestDay = status.CountsAsRest;
        IsComplete = status.Progress == DayProgress.Complete || status.CompletedSessions.Count >= status.SessionCount;
        BonusCount = bonusCount;
        IsLate = completions.Any(c => c.CompletedOn > status.Date);
        ShowsTicks = status.ShowsSessionTicks || (status.RequiredCount == 0 && !status.IsRestDay);

        // Seed and ink before strokes: a seed change redraws, which would cut a stroke animation short.
        Seed = completions.Count > 0 ? MarkerGeometry.SeedFrom(completions[0].CompletionId) : DayIndex;
        if (!Inks.SequenceEqual(inks))
        {
            Inks = inks;
        }

        MarkStrokes = MarkerAppearance.StrokesFor(status, autos);
        AutomationName = BuildAutomationName(status, sessionNames);
    }

    private string BuildAutomationName(DayStatus status, IReadOnlyList<string> sessionNames)
    {
        List<string> parts = [_localizer["CalendarDayNumberFormat", DayNumber].Value];
        if (sessionNames.Count > 0)
        {
            parts.Add(string.Join(_localizer["CalendarSessionJoin"].Value, sessionNames));
        }

        var done = status.CompletedSessions.Count(i => !status.OptionalSessions.Contains(i));
        if (IsComplete)
        {
            parts.Add(_localizer["CalendarDayDone"].Value);
        }
        else if (done > 0)
        {
            parts.Add(_localizer["CrossMomentProgressFormat", done, status.RequiredCount].Value);
        }
        else if (!IsRestDay && !IsFuture)
        {
            parts.Add(_localizer[IsMissed ? "CalendarDayMissed" : "CalendarDayNotDone"].Value);
        }

        if (IsToday)
        {
            parts.Add(_localizer["CalendarDayToday"].Value);
        }
        else if (IsFuture)
        {
            parts.Add(_localizer["CalendarDayUpcoming"].Value);
        }

        if (BonusCount > 0)
        {
            parts.Add(_localizer["CalendarDayBonusFormat", BonusCount].Value);
        }

        parts.Add(DateLabel);
        return string.Join(", ", parts);
    }
}
