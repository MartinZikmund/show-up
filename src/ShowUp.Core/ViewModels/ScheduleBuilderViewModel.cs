using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using ShowUp.Core.Catalog;
using ShowUp.Core.CustomPrograms;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Localization;
using ShowUp.Core.Services;
using ShowUp.Services.Dialogs;

namespace ShowUp.Core.ViewModels;

/// <summary>Builds or edits a custom program (Pro): name, color, weeks, and per day workouts, rest or optional sessions.</summary>
public partial class ScheduleBuilderViewModel : ViewModelBase
{
    /// <summary>Hex values of the eight accent swatches, with their resource-name suffixes.</summary>
    public static IReadOnlyList<(string Hex, string Name)> Palette { get; } =
    [
        ("#D9372B", "Red"),
        ("#E0761F", "Orange"),
        ("#C99A0E", "Mustard"),
        ("#3E8E41", "Green"),
        ("#17847F", "Teal"),
        ("#2F6FDB", "Blue"),
        ("#6E4BC4", "Purple"),
        ("#C2417E", "Pink"),
    ];

    private readonly IStringLocalizer _localizer;
    private readonly ICatalogService _catalog;
    private readonly ICustomProgramService _programs;
    private readonly IEntitlementService _entitlements;
    private readonly INavigationService _navigation;
    private readonly ISessionPickerDialog _picker;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ScheduleBuilderViewModel> _logger;
    private readonly List<BuilderWeekViewModel> _weekStore = [];
    private readonly List<Workout> _ownWorkouts = [];
    private IReadOnlyDictionary<string, Workout> _catalogWorkouts = new Dictionary<string, Workout>();
    private bool _loaded;
    private bool _isSaving;
    private bool _listening;
    private SynchronizationContext? _context;

    public ScheduleBuilderViewModel(
        IStringLocalizer localizer,
        ICatalogService catalog,
        ICustomProgramService programs,
        IEntitlementService entitlements,
        INavigationService navigation,
        ISessionPickerDialog picker,
        IDialogService dialogs,
        ILogger<ScheduleBuilderViewModel> logger)
    {
        _localizer = localizer;
        _catalog = catalog;
        _programs = programs;
        _entitlements = entitlements;
        _navigation = navigation;
        _picker = picker;
        _dialogs = dialogs;
        _logger = logger;
        ProgramId = CustomProgramFactory.NewProgramId();
        PageTitle = localizer["BuilderNewTitle"];
        AccentSwatches = [.. Palette.Select(p => new AccentSwatch(p.Hex, localizer[$"BuilderAccent{p.Name}"]))];
        SelectedAccent = AccentSwatches[0];
        UpdateLock();
    }

    public string ProgramId { get; private set; }

    [ObservableProperty]
    public partial bool IsEditing { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewName))]
    public partial string Name { get; set; } = "";

    /// <summary>The hero preview's title; a placeholder until the schedule is named.</summary>
    public string PreviewName => Name.Trim() is { Length: > 0 } name ? name : _localizer["BuilderNamePlaceholder"];

    public IReadOnlyList<AccentSwatch> AccentSwatches { get; }

    [ObservableProperty]
    public partial AccentSwatch? SelectedAccent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WeekCountValue))]
    public partial int WeekCount { get; set; }

    /// <summary>The NumberBox's double; a cleared box reads as zero weeks.</summary>
    public double WeekCountValue
    {
        get => WeekCount;
        set => WeekCount = double.IsNaN(value) ? 0 : (int)Math.Clamp(Math.Round(value), 0, 1000);
    }

    public ObservableCollection<BuilderWeekViewModel> Weeks { get; } = [];

    /// <summary>The week the wide layout's day editor shows.</summary>
    [ObservableProperty]
    public partial BuilderWeekViewModel? SelectedWeek { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> ValidationMessages { get; private set; } = [];

    [ObservableProperty]
    public partial string? NameError { get; private set; }

    [ObservableProperty]
    public partial string? WeeksError { get; private set; }

    /// <summary>Without Pro the builder is read-only and points at the paywall.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsLocked { get; private set; }

    public bool IsEditable => !IsLocked;

    public bool HasValidationMessages => ValidationMessages.Count > 0;

    public bool CanSave => !IsLocked && _loaded && ValidationMessages.Count == 0;

    public override void OnNavigatedTo(object? parameter)
    {
        base.OnNavigatedTo(parameter);
        if (!_listening)
        {
            _listening = true;
            _context = SynchronizationContext.Current;
            _entitlements.Changed += OnEntitlementsChanged;
        }

        RunInBackground(() => LoadAsync(parameter as string));
    }

    public override void OnNavigatedFrom()
    {
        base.OnNavigatedFrom();
        if (_listening)
        {
            _listening = false;
            _entitlements.Changed -= OnEntitlementsChanged;
        }
    }

    /// <summary>A null <paramref name="programId"/> starts a new one-week schedule.</summary>
    public async Task LoadAsync(string? programId)
    {
        IsLoading = true;
        try
        {
            var catalog = await _catalog.GetCatalogAsync();
            _catalogWorkouts = catalog.WorkoutsById;
            if (programId is not null && await _catalog.GetProgramAsync(programId) is { IsCustom: true } program)
            {
                var draft = CustomProgramFactory.ToDraft(program);
                ProgramId = draft.ProgramId;
                IsEditing = true;
                PageTitle = _localizer["BuilderEditTitle"];
                Name = draft.Name;
                SelectedAccent = AccentSwatches.FirstOrDefault(s => string.Equals(s.Hex, draft.AccentColor, StringComparison.OrdinalIgnoreCase)) ?? AccentSwatches[0];
                _ownWorkouts.Clear();
                _ownWorkouts.AddRange(draft.Workouts);
                WeekCount = draft.Weeks.Count;
                for (int w = 0; w < draft.Weeks.Count; w++)
                {
                    _weekStore[w].Set(draft.Weeks[w]);
                }
            }
            else if (WeekCount == 0)
            {
                WeekCount = 1;
            }

            _loaded = true;
            Revalidate();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public CustomProgramDraft ToDraft() => new(
        ProgramId,
        Name,
        SelectedAccent?.Hex ?? Palette[0].Hex,
        [.. Weeks.Select(w => w.ToDays())],
        [.. _ownWorkouts]);

    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync()
    {
        if (_isSaving || !CanSave)
        {
            return;
        }

        _isSaving = true;
        try
        {
            await _programs.SaveAsync(ToDraft());
        }
        catch (CustomProgramLockedException)
        {
            UpdateLock();
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving custom program {ProgramId} failed.", ProgramId);
            await _dialogs.ShowAsync(_localizer["BuilderSaveFailedTitle"], _localizer["BuilderSaveFailedMessage"]);
            return;
        }
        finally
        {
            _isSaving = false;
        }

        // An edit returns to its detail page (which reloads); a new schedule opens its own.
        _navigation.GoBack();
        if (!IsEditing)
        {
            _navigation.Navigate<ProgramDetailViewModel>(ProgramId);
        }
    }

    [RelayCommand]
    private void Cancel() => _navigation.GoBack();

    [RelayCommand]
    private void OpenPro() => _navigation.Navigate<ProViewModel>();

    [RelayCommand]
    private void SelectAccent(AccentSwatch? swatch)
    {
        if (swatch is not null)
        {
            SelectedAccent = swatch;
        }
    }

    internal string Text(string key, params object[] args) => args.Length == 0 ? _localizer[key] : _localizer[key, args];

    internal string Format(string key, int count) => _localizer.Format(key, count);

    internal bool HasWeekAfter(BuilderWeekViewModel week) => week.Number < Weeks.Count;

    internal async Task AddWorkoutAsync(BuilderDayViewModel day)
    {
        if (IsLocked)
        {
            return;
        }

        // Other custom programs' workouts aren't offered: each program carries only its own.
        IReadOnlyList<Workout> workouts = [.. _catalogWorkouts.Values.Where(w => !IsCustomWorkout(w)), .. _ownWorkouts];
        if (await _picker.ShowAsync(workouts) is not { } pick)
        {
            return;
        }

        string id;
        if (pick.Existing is { } existing)
        {
            id = existing.Id;
        }
        else
        {
            id = CustomProgramFactory.NextWorkoutId(ProgramId, _ownWorkouts);
            _ownWorkouts.Add(new Workout(id, pick.Title, pick.DurationMinutes, pick.Kind, null));
        }

        day.Add(new CustomSession(id, pick.Optional));
    }

    internal BuilderSessionViewModel CreateSession(BuilderDayViewModel day, CustomSession session)
    {
        var workout = _ownWorkouts.FirstOrDefault(w => w.Id == session.WorkoutId) ?? _catalogWorkouts.GetValueOrDefault(session.WorkoutId);
        bool isRest = workout?.Kind == WorkoutKind.Rest;
        string title = isRest ? _localizer["BuilderRest"] : workout?.Name ?? session.WorkoutId;
        return new(day, session.WorkoutId, title, isRest || workout is null ? "" : Detail(workout), isRest, session.Optional && !isRest);
    }

    internal void CopyWeek(BuilderWeekViewModel source, bool toAll)
    {
        var days = source.ToDays();
        foreach (var target in Weeks.Where(w => toAll ? w != source : w.Number == source.Number + 1).ToList())
        {
            target.Set(days);
        }
    }

    internal void Revalidate()
    {
        if (!_loaded)
        {
            return;
        }

        // Weeks past the maximum aren't shown, so the count itself is checked too.
        List<CustomProgramError> errors = [.. CustomProgramFactory.Validate(ToDraft())];
        if (WeekCount > CustomProgramFactory.MaxWeeks)
        {
            errors.Add(new(CustomProgramProblem.TooManyWeeks));
        }

        NameError = errors.Any(e => e.Problem == CustomProgramProblem.EmptyName) ? _localizer["BuilderErrorName"].Value : null;
        WeeksError = errors.Any(e => e.Problem == CustomProgramProblem.NoWeeks) ? _localizer["BuilderErrorNoWeeks"].Value
            : errors.Any(e => e.Problem == CustomProgramProblem.TooManyWeeks) ? _localizer["BuilderErrorTooManyWeeks", CustomProgramFactory.MaxWeeks].Value
            : null;
        int emptyDays = errors.Count(e => e.Problem == CustomProgramProblem.EmptyDay);

        List<string> messages = [];
        if (NameError is { } name)
        {
            messages.Add(name);
        }

        if (WeeksError is { } weeks)
        {
            messages.Add(weeks);
        }

        if (emptyDays > 0)
        {
            messages.Add(_localizer.Format("BuilderErrorEmptyDays", emptyDays));
        }

        ValidationMessages = messages;
        OnPropertyChanged(nameof(HasValidationMessages));
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnNameChanged(string value)
    {
        if (value.Length > CustomProgramFactory.MaxNameLength)
        {
            Name = value[..CustomProgramFactory.MaxNameLength];
            return;
        }

        Revalidate();
    }

    partial void OnSelectedAccentChanged(AccentSwatch? value)
    {
        foreach (var swatch in AccentSwatches)
        {
            swatch.IsSelected = swatch == value;
        }
    }

    // Weeks beyond the count are kept, so stepping down and back up doesn't lose them.
    partial void OnWeekCountChanged(int value)
    {
        int shown = Math.Clamp(value, 0, CustomProgramFactory.MaxWeeks);
        while (_weekStore.Count < shown)
        {
            _weekStore.Add(new BuilderWeekViewModel(this, _weekStore.Count + 1));
        }

        while (Weeks.Count > shown)
        {
            Weeks.RemoveAt(Weeks.Count - 1);
        }

        while (Weeks.Count < shown)
        {
            Weeks.Add(_weekStore[Weeks.Count]);
        }

        if (SelectedWeek is null)
        {
            SelectedWeek = Weeks.FirstOrDefault();
        }
        else if (!Weeks.Contains(SelectedWeek))
        {
            SelectedWeek = Weeks.LastOrDefault();
        }

        foreach (var week in Weeks)
        {
            week.RefreshCopyState();
        }

        Revalidate();
    }

    partial void OnSelectedWeekChanged(BuilderWeekViewModel? value)
    {
        foreach (var week in _weekStore)
        {
            week.IsSelected = week == value;
        }
    }

    partial void OnValidationMessagesChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasValidationMessages));

    private static bool IsCustomWorkout(Workout workout) => workout.Id.StartsWith(Storage.SqliteCustomProgramRepository.IdPrefix, StringComparison.Ordinal);

    private string Detail(Workout workout)
    {
        string kind = _localizer[$"WorkoutKind_{workout.Kind}"];
        return workout.DurationMinutes is { } minutes ? _localizer["BuilderSessionDetailFormat", minutes, kind] : kind;
    }

    private void OnEntitlementsChanged(object? sender, EventArgs e)
    {
        if (_context is null || _context == SynchronizationContext.Current)
        {
            UpdateLock();
        }
        else
        {
            _context.Post(_ => UpdateLock(), null);
        }
    }

    private void UpdateLock()
    {
        IsLocked = !_programs.CanEdit;
        OnPropertyChanged(nameof(CanSave));
    }

    private async void RunInBackground(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading the schedule builder failed.");
        }
    }
}
