using Microsoft.Extensions.Localization;
using ShowUp.Core.Catalog;
using ShowUp.Core.Navigation;
using ShowUp.Core.Services;
using ShowUp.Core.Storage;
using ShowUp.Core.Tracking;
using ShowUp.Services.Dialogs;

namespace ShowUp.Core.Tests.ViewModels;

internal sealed class PassThroughLocalizer : IStringLocalizer
{
    public LocalizedString this[string name] => new(name, name);

    public LocalizedString this[string name, params object[] arguments] => new(name, $"{name}({string.Join(",", arguments)})");

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

internal sealed class RecordingNavigationService : INavigationService
{
    public List<(Type ViewModel, object? Parameter)> Navigations { get; } = [];

    public bool CanGoBack => false;

    public NavigationSection? CurrentSection => null;

    public void Initialize() { }

    public void Navigate<TViewModel>() => Navigations.Add((typeof(TViewModel), null));

    public void Navigate<TViewModel>(object? parameter) => Navigations.Add((typeof(TViewModel), parameter));

    public int BackCount { get; private set; }

    public bool GoBack()
    {
        BackCount++;
        return true;
    }

    public void ClearBackStack() { }
}

internal sealed class RecordingSectionNavigator : ISectionNavigator
{
    public List<NavigationSection> Sections { get; } = [];

    public void NavigateToSection(NavigationSection section) => Sections.Add(section);
}

internal sealed class StubConfirmationDialogService : IConfirmationDialogService
{
    public ConfirmationResult Result { get; set; } = ConfirmationResult.Confirmed;

    public List<(string Title, string Text)> Asked { get; } = [];

    public Task<ConfirmationResult> ShowAsync(string title, string text)
    {
        Asked.Add((title, text));
        return Task.FromResult(Result);
    }
}

/// <summary>Records the write calls; <see cref="Active"/> is what GetAsync returns.</summary>
internal sealed class RecordingEnrollmentService : IActiveEnrollmentService
{
    public event EventHandler? Changed { add { } remove { } }

    public ActiveEnrollment? Active { get; set; }

    public List<string> Calls { get; } = [];

    public (WorkoutProgram Program, ProgramTrack Track, DateOnly Start)? Enrolled { get; private set; }

    public Task<ActiveEnrollment?> GetAsync() => Task.FromResult(Active);

    public Task<ActiveEnrollment> EnrollAsync(WorkoutProgram program, ProgramTrack track, DateOnly startDate)
    {
        Calls.Add("Enroll");
        Enrolled = (program, track, startDate);
        return Task.FromResult(new ActiveEnrollment(new EnrollmentEntity(), program, track, new EnrollmentCalendar(startDate, 1, [])));
    }

    public Task EndAsync(EnrollmentEndReason reason)
    {
        Calls.Add($"End:{reason}");
        Active = null;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CompletedSession>> GetCompletionsAsync() => Task.FromResult<IReadOnlyList<CompletedSession>>([]);

    public Task<IReadOnlyList<BonusWorkoutEntity>> GetBonusWorkoutsAsync() => Task.FromResult<IReadOnlyList<BonusWorkoutEntity>>([]);

    public Task CrossAsync(int dayIndex, int sessionIndex, string? inkJson = null, Guid? completionId = null) => Task.CompletedTask;

    public Task UncrossAsync(int dayIndex, int sessionIndex) => Task.CompletedTask;

    public Task ShiftAsync(DateOnly fromDate, int days) => Task.CompletedTask;

    public Task LogBonusAsync(string title, DateOnly date) => Task.CompletedTask;
}
