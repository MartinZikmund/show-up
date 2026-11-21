using ShowUp.Core.Tracking;

namespace ShowUp.Core.ViewModels;

/// <summary>One active program on Today: its day, session cards and marks.</summary>
public sealed partial class TodayProgramViewModel(Guid enrollmentId) : ObservableObject
{
    public Guid EnrollmentId { get; } = enrollmentId;

    public string AutomationId => $"TodayProgram_{EnrollmentId:N}";

    [ObservableProperty]
    public partial string ProgramName { get; internal set; } = "";

    [ObservableProperty]
    public partial int? DayNumber { get; internal set; }

    /// <summary>"Day 12 of 60", empty when today isn't a program day.</summary>
    [ObservableProperty]
    public partial string DayText { get; internal set; } = "";

    [ObservableProperty]
    public partial int CurrentStreak { get; internal set; }

    [ObservableProperty]
    public partial string StreakText { get; internal set; } = "";

    /// <summary>Today is a rest day (or all of its sessions are optional).</summary>
    [ObservableProperty]
    public partial bool IsRestDay { get; internal set; }

    /// <summary>Nothing scheduled today: before it starts, after it ends, or a gap from a shift.</summary>
    [ObservableProperty]
    public partial bool IsOffSchedule { get; internal set; }

    /// <summary>Pro lapsed and this isn't the primary program: shown, not crossed.</summary>
    [ObservableProperty]
    public partial bool IsReadOnly { get; internal set; }

    [ObservableProperty]
    public partial IReadOnlyList<TodaySessionViewModel> Sessions { get; internal set; } = [];

    /// <summary>Today's generated marker strokes; drawn crosses are in <see cref="Inks"/>.</summary>
    [ObservableProperty]
    public partial int MarkStrokes { get; internal set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> Inks { get; internal set; } = [];

    [ObservableProperty]
    public partial int Seed { get; internal set; }

    /// <summary>False while a cross moment shows the cross big, so the card lands it without animating.</summary>
    public bool AnimateMarks { get; internal set; } = true;

    internal int DayIndex { get; set; } = -1;

    internal IReadOnlyList<DayStatus> Statuses { get; set; } = [];

    internal IReadOnlyList<CompletedSession> Completions { get; set; } = [];

    internal SemaphoreSlim CrossGate { get; } = new(1, 1);
}
