using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShowUp.Core.Marking;
using ShowUp.Core.Navigation;
using ShowUp.Core.Services;
using ShowUp.Services.Dialogs;
using ShowUp.Services.Settings;

namespace ShowUp.Core.Tests.ViewModels;

internal sealed class FakeStringLocalizer : IStringLocalizer
{
    public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

    public LocalizedString this[string name, params object[] arguments] => new(name, $"{name}({string.Join(",", arguments)})", resourceNotFound: true);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

internal sealed class FakeCrossMoment : ICrossMoment
{
    public List<CrossMomentInfo> Shown { get; } = [];

    public CrossMomentResult DrawResult { get; set; } = CrossMomentResult.Auto;

    public List<CrossMomentDrawRequest> DrawRequests { get; } = [];

    public Task ShowAsync(CrossMomentInfo info)
    {
        Shown.Add(info);
        return Task.CompletedTask;
    }

    public Task<CrossMomentResult> DrawAsync(CrossMomentDrawRequest request)
    {
        DrawRequests.Add(request);
        return Task.FromResult(DrawResult);
    }
}

internal sealed class FakeMarkingFeedback : IMarkingFeedback
{
    public bool SoundEnabled { get; set; }

    public bool HapticsEnabled { get; set; }

    public void StrokeDrawn() { }

    public void SessionTicked() { }

    public void CrossLanded() { }

    public void ScratchStart() { }

    public void ScratchSpeed(double normalizedSpeed) { }

    public void ScratchStop() { }

    public void DayTaken() { }
}

internal sealed class FakeConfirmationDialogService : IConfirmationDialogService
{
    public ConfirmationResult Result { get; set; } = ConfirmationResult.Confirmed;

    public Task<ConfirmationResult> ShowAsync(string title, string text) => Task.FromResult(Result);
}

internal sealed class FakeBonusWorkoutDialog : IBonusWorkoutDialog
{
    public string? Title { get; set; }

    public string? WorkoutId { get; set; }

    public Task<BonusWorkoutEntry?> ShowAsync(DateOnly date) =>
        Task.FromResult<BonusWorkoutEntry?>(Title is null ? null : new(WorkoutId, Title, null, date));
}

internal sealed class FakeSectionNavigator : ISectionNavigator
{
    public List<NavigationSection> Sections { get; } = [];

    public void NavigateToSection(NavigationSection section) => Sections.Add(section);
}

internal sealed class FakeAppPreferences : IAppPreferences
{
    public int DataVersion { get; set; }

    public bool FirstStart { get; set; }

    public int LaunchCount { get; set; }

    public bool OfferUserRating { get; set; }

    public ElementTheme Theme { get; set; }

    public bool MarkerSoundEnabled { get; set; }

    public bool MarkerHapticsEnabled { get; set; }

    public CrossMomentMode CrossMomentMode { get; set; }

    public CrossOffStyle CrossOffStyle { get; set; }

    public MarkerColor MarkerColor { get; set; }
}

internal sealed class RecordingJournalDialogs : IJournalDialogs
{
    public List<string> Calls { get; } = [];

    public Task ShowDayAsync(int dayIndex) => Record($"day:{dayIndex}");

    public Task LogResultsAsync(int dayIndex, int sessionIndex) => Record($"results:{dayIndex}:{sessionIndex}");

    public Task LogBonusResultsAsync(ShowUp.Core.Storage.BonusWorkoutEntity bonus) => Record($"bonus:{bonus.WorkoutId}");

    public Task EditProgramNoteAsync() => Record("program-note");

    private Task Record(string call)
    {
        Calls.Add(call);
        return Task.CompletedTask;
    }
}

/// <summary>A journal with no notes; a session has results to log only when <see cref="SheetFor"/> returns a sheet.</summary>
internal sealed class StubJournalService : ShowUp.Core.Tracking.IJournalService
{
    public Func<int, int, ShowUp.Core.Tracking.ResultsSheet?> SheetFor { get; set; } = (_, _) => null;

    public Task<string> GetProgramNoteAsync() => Task.FromResult("");

    public Task SaveProgramNoteAsync(string? text) => Task.CompletedTask;

    public Task<ShowUp.Core.Tracking.DayJournal?> GetDayAsync(int dayIndex) => Task.FromResult<ShowUp.Core.Tracking.DayJournal?>(null);

    public Task SaveNoteAsync(int dayIndex, int? sessionIndex, string? text) => Task.CompletedTask;

    public Task<ShowUp.Core.Tracking.ResultsSheet?> GetResultsSheetAsync(int dayIndex, int sessionIndex) => Task.FromResult(SheetFor(dayIndex, sessionIndex));

    public Task<ShowUp.Core.Tracking.ResultsSheet?> GetBonusResultsSheetAsync(ShowUp.Core.Storage.BonusWorkoutEntity bonus) =>
        Task.FromResult<ShowUp.Core.Tracking.ResultsSheet?>(null);

    public Task SaveResultsAsync(ShowUp.Core.Tracking.MetricTarget target, IReadOnlyDictionary<string, ShowUp.Core.Tracking.MetricValue> values) => Task.CompletedTask;

    public Task<IReadOnlyList<ShowUp.Core.Tracking.ResultsTable>> GetResultsAsync() => Task.FromResult<IReadOnlyList<ShowUp.Core.Tracking.ResultsTable>>([]);
}
