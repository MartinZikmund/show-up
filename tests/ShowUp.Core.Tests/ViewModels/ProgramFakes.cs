using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Controls;
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

    public Task<DialogChoice> ChooseAsync(string title, string text, string primary, string secondary) =>
        throw new NotSupportedException();
}

/// <summary>Records the write calls; <see cref="All"/> (or <see cref="Active"/> for one) is what the getters return.</summary>
internal sealed class RecordingEnrollmentService : IActiveEnrollmentService
{
    public List<ActiveEnrollment> All { get; set; } = [];

    /// <summary>The single active enrollment: setting it replaces <see cref="All"/>.</summary>
    public ActiveEnrollment? Active
    {
        get => All.FirstOrDefault();
        set => All = value is null ? [] : [value];
    }

    public HashSet<Guid> ReadOnlyIds { get; } = [];

    public Guid? PrimaryId { get; set; }

    public List<string> Calls { get; } = [];

    public Exception? EnrollFailure { get; set; }

    public Exception? EndFailure { get; set; }

    public EnrollmentEndReason? EndReason { get; private set; }

    public Guid? ReplacedId { get; private set; }

    public (WorkoutProgram Program, ProgramTrack Track, DateOnly Start)? Enrolled { get; private set; }

    public Guid? SelectedEnrollmentId { get; private set; }

    public List<ActiveEnrollment> History { get; set; } = [];

    public void Invalidate()
    {
    }

    public Task<IReadOnlyList<ActiveEnrollment>> GetHistoryAsync() => Task.FromResult<IReadOnlyList<ActiveEnrollment>>([.. History]);

    public Task<IReadOnlyList<CompletedSession>> GetHistoryCompletionsAsync(Guid enrollmentId) => Task.FromResult<IReadOnlyList<CompletedSession>>([]);

    public Task<IReadOnlyList<ActiveEnrollment>> GetAllAsync() => Task.FromResult<IReadOnlyList<ActiveEnrollment>>([.. All]);

    public Task<ActiveEnrollment?> GetSelectedAsync() =>
        Task.FromResult(All.FirstOrDefault(e => e.Id == SelectedEnrollmentId) ?? All.FirstOrDefault());

    public Task<ActiveEnrollment?> GetAsync(Guid enrollmentId) => Task.FromResult(All.FirstOrDefault(e => e.Id == enrollmentId));

    public Task SelectAsync(Guid enrollmentId)
    {
        Calls.Add("Select");
        SelectedEnrollmentId = enrollmentId;
        return Task.CompletedTask;
    }

    public Task<ActiveEnrollment?> GetPrimaryAsync() =>
        Task.FromResult(All.FirstOrDefault(e => e.Id == PrimaryId) ?? All.FirstOrDefault());

    public Task<bool> HasPrimaryChoiceAsync() => Task.FromResult(All.Any(e => e.Id == PrimaryId));

    public Task SetPrimaryAsync(Guid enrollmentId)
    {
        PrimaryId = enrollmentId;
        return Task.CompletedTask;
    }

    public bool IsReadOnly(ActiveEnrollment enrollment) => ReadOnlyIds.Contains(enrollment.Id);

    public Task<ActiveEnrollment> EnrollAsync(WorkoutProgram program, ProgramTrack track, DateOnly startDate)
    {
        if (EnrollFailure is not null)
        {
            return Task.FromException<ActiveEnrollment>(EnrollFailure);
        }

        Calls.Add("Enroll");
        Enrolled = (program, track, startDate);
        return Task.FromResult(new ActiveEnrollment(new EnrollmentEntity { Id = Guid.NewGuid() }, program, track, new EnrollmentCalendar(startDate, 1, [])));
    }

    public Task<ActiveEnrollment> ReplaceAsync(Guid enrollmentId, EnrollmentEndReason reason, WorkoutProgram program, ProgramTrack track, DateOnly startDate)
    {
        if (EndFailure is not null)
        {
            return Task.FromException<ActiveEnrollment>(EndFailure);
        }

        if (EnrollFailure is not null)
        {
            return Task.FromException<ActiveEnrollment>(EnrollFailure);
        }

        Calls.Add($"Replace:{reason}");
        EndReason = reason;
        ReplacedId = enrollmentId;
        Enrolled = (program, track, startDate);
        All.RemoveAll(e => e.Id == enrollmentId);
        return Task.FromResult(new ActiveEnrollment(new EnrollmentEntity { Id = Guid.NewGuid() }, program, track, new EnrollmentCalendar(startDate, 1, [])));
    }

    public Task EndAsync(Guid enrollmentId, EnrollmentEndReason reason)
    {
        if (EndFailure is not null)
        {
            return Task.FromException(EndFailure);
        }

        Calls.Add($"End:{reason}");
        EndReason = reason;
        All.RemoveAll(e => e.Id == enrollmentId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CompletedSession>> GetCompletionsAsync(Guid enrollmentId) => Task.FromResult<IReadOnlyList<CompletedSession>>([]);

    public Task<IReadOnlyList<BonusWorkoutEntity>> GetBonusWorkoutsAsync(Guid enrollmentId) => Task.FromResult<IReadOnlyList<BonusWorkoutEntity>>([]);

    public List<(int Day, int Session, string? Ink, Guid? CompletionId)> Crosses { get; } = [];

    public List<Guid> CrossedEnrollments { get; } = [];

    public Task CrossAsync(Guid enrollmentId, int dayIndex, int sessionIndex, string? inkJson = null, Guid? completionId = null)
    {
        CrossedEnrollments.Add(enrollmentId);
        Crosses.Add((dayIndex, sessionIndex, inkJson, completionId));
        return Task.CompletedTask;
    }

    public Task<bool> FinishAsync(Guid enrollmentId)
    {
        Calls.Add("Finish");
        return Task.FromResult(FinishResult);
    }

    public bool FinishResult { get; set; } = true;

    public Task UncrossAsync(Guid enrollmentId, int dayIndex, int sessionIndex) => Task.CompletedTask;

    public Task ShiftAsync(Guid enrollmentId, DateOnly fromDate, int days) => Task.CompletedTask;

    public Task<IReadOnlyList<BonusWorkoutEntity>> GetBonusWorkoutsAsync(DateOnly from, DateOnly to) => Task.FromResult<IReadOnlyList<BonusWorkoutEntity>>([]);

    public Task<BonusWorkoutEntity> LogBonusAsync(string? workoutId, string title, DateOnly date, int? durationMinutes) =>
        Task.FromResult(new BonusWorkoutEntity { WorkoutId = workoutId, Title = title, Date = date, DurationMinutes = durationMinutes });

    public Task RemoveBonusAsync(Guid id) => Task.CompletedTask;
}

internal sealed class RecordingDialogService : IDialogService
{
    public List<(string Title, string Content)> Shown { get; } = [];

    public Task<ContentDialogResult> ShowAsync(string title, string content)
    {
        Shown.Add((title, content));
        return Task.FromResult(ContentDialogResult.Primary);
    }

    public Task<ContentDialogResult> ShowAsync(ContentDialog contentDialog) => Task.FromResult(ContentDialogResult.Primary);
}
