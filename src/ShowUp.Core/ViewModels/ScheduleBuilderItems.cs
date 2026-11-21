using System.Collections.ObjectModel;
using ShowUp.Core.Catalog;
using ShowUp.Core.CustomPrograms;

namespace ShowUp.Core.ViewModels;

/// <summary>One of the builder's eight accent colors.</summary>
public sealed partial class AccentSwatch(string hex, string name) : ObservableObject, ISwatchItem
{
    public string Hex { get; } = hex;

    public string Name { get; } = name;

    public string AutomationId => $"BuilderAccent_{Hex.TrimStart('#')}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>A session chip in a builder day.</summary>
public sealed partial class BuilderSessionViewModel : ObservableObject
{
    private readonly BuilderDayViewModel _day;

    internal BuilderSessionViewModel(BuilderDayViewModel day, string workoutId, string title, string detail, bool isRest, bool optional)
    {
        _day = day;
        WorkoutId = workoutId;
        Title = title;
        Detail = detail;
        IsRest = isRest;
        IsOptional = optional;
    }

    public string WorkoutId { get; }

    public string Title { get; }

    /// <summary>Minutes and kind, e.g. "30 min · Cardio"; empty for rest.</summary>
    public string Detail { get; }

    public bool IsRest { get; }

    public bool IsWorkout => !IsRest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OptionalToggleText))]
    public partial bool IsOptional { get; set; }

    [ObservableProperty]
    public partial bool CanMoveUp { get; internal set; }

    [ObservableProperty]
    public partial bool CanMoveDown { get; internal set; }

    [ObservableProperty]
    public partial int Position { get; internal set; }

    public string OptionalToggleText => _day.Owner.Text(IsOptional ? "BuilderMakeRequired" : "BuilderMakeOptional");

    public string AutomationId => $"BuilderSession_W{_day.Week.Number}D{_day.Number}S{Position}";

    public string AutomationName => IsOptional ? _day.Owner.Text("ProgramOptionalFormat", Title) : Title;

    public string UpAutomationId => $"{AutomationId}_Up";

    public string DownAutomationId => $"{AutomationId}_Down";

    public string MoreAutomationId => $"{AutomationId}_More";

    public string MoveUpName => _day.Owner.Text("BuilderMoveUpFormat", Title);

    public string MoveDownName => _day.Owner.Text("BuilderMoveDownFormat", Title);

    public string MoreName => _day.Owner.Text("BuilderSessionMoreFormat", Title);

    internal CustomSession ToSession() => new(WorkoutId, IsOptional);

    [RelayCommand]
    private void MoveUp() => _day.Move(this, -1);

    [RelayCommand]
    private void MoveDown() => _day.Move(this, 1);

    [RelayCommand]
    private void ToggleOptional()
    {
        IsOptional = !IsOptional;
        OnPropertyChanged(nameof(AutomationName));
        _day.Changed();
    }

    [RelayCommand]
    private void Remove() => _day.Remove(this);

    partial void OnPositionChanged(int value)
    {
        OnPropertyChanged(nameof(AutomationId));
        OnPropertyChanged(nameof(UpAutomationId));
        OnPropertyChanged(nameof(DownAutomationId));
        OnPropertyChanged(nameof(MoreAutomationId));
    }
}

/// <summary>One of a builder week's seven days.</summary>
public sealed partial class BuilderDayViewModel : ObservableObject
{
    internal BuilderDayViewModel(ScheduleBuilderViewModel owner, BuilderWeekViewModel week, int number)
    {
        Owner = owner;
        Week = week;
        Number = number;
        Sessions.CollectionChanged += (_, _) => Renumber();
    }

    internal ScheduleBuilderViewModel Owner { get; }

    internal BuilderWeekViewModel Week { get; }

    public int Number { get; }

    public string Label => Owner.Text("CalendarDayNumberFormat", Number);

    public ObservableCollection<BuilderSessionViewModel> Sessions { get; } = [];

    public bool IsEmpty => Sessions.Count == 0;

    public bool IsRest => Sessions.Count > 0 && Sessions.All(s => s.IsRest);

    public string AutomationPrefix => $"BuilderW{Week.Number}D{Number}";

    public string AddAutomationId => $"{AutomationPrefix}Add";

    public string RestAutomationId => $"{AutomationPrefix}Rest";

    public string ClearAutomationId => $"{AutomationPrefix}Clear";

    public string AddAutomationName => Owner.Text("BuilderAddToDayFormat", Week.Number, Number);

    [RelayCommand]
    private Task AddWorkoutAsync() => Owner.AddWorkoutAsync(this);

    /// <summary>A rest day has nothing else on it.</summary>
    [RelayCommand]
    private void MakeRest()
    {
        Sessions.Clear();
        Sessions.Add(Owner.CreateSession(this, new CustomSession(CustomProgramFactory.RestWorkoutId)));
        Changed();
    }

    [RelayCommand]
    private void Clear()
    {
        Sessions.Clear();
        Changed();
    }

    internal void Add(CustomSession session)
    {
        foreach (var rest in Sessions.Where(s => s.IsRest).ToList())
        {
            Sessions.Remove(rest);
        }

        Sessions.Add(Owner.CreateSession(this, session));
        Changed();
    }

    internal void Set(IEnumerable<CustomSession> sessions)
    {
        Sessions.Clear();
        foreach (var session in sessions)
        {
            Sessions.Add(Owner.CreateSession(this, session));
        }
    }

    internal IReadOnlyList<CustomSession> ToSessions() => [.. Sessions.Select(s => s.ToSession())];

    internal void Move(BuilderSessionViewModel session, int delta)
    {
        int from = Sessions.IndexOf(session);
        int to = from + delta;
        if (from >= 0 && to >= 0 && to < Sessions.Count)
        {
            Sessions.Move(from, to);
            Changed();
        }
    }

    internal void Remove(BuilderSessionViewModel session)
    {
        if (Sessions.Remove(session))
        {
            Changed();
        }
    }

    internal void Changed()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsRest));
        Week.Changed();
    }

    private void Renumber()
    {
        for (int i = 0; i < Sessions.Count; i++)
        {
            Sessions[i].Position = i + 1;
            Sessions[i].CanMoveUp = i > 0;
            Sessions[i].CanMoveDown = i < Sessions.Count - 1;
        }
    }
}

/// <summary>A builder week: seven days, a summary line and copy actions.</summary>
public sealed partial class BuilderWeekViewModel : ObservableObject
{
    internal BuilderWeekViewModel(ScheduleBuilderViewModel owner, int number)
    {
        Owner = owner;
        Number = number;
        Days = [.. Enumerable.Range(1, CustomProgramFactory.DaysPerWeek).Select(d => new BuilderDayViewModel(owner, this, d))];
    }

    internal ScheduleBuilderViewModel Owner { get; }

    public int Number { get; }

    public string Label => Owner.Text("CalendarWeekFormat", Number);

    public IReadOnlyList<BuilderDayViewModel> Days { get; }

    /// <summary>Collapsed weeks keep long schedules scrollable on a phone.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public int EmptyDays => Days.Count(d => d.IsEmpty);

    public bool HasEmptyDays => EmptyDays > 0;

    public string Summary => HasEmptyDays
        ? Owner.Format("BuilderWeekEmptyDays", EmptyDays)
        : Owner.Text(
            "BuilderWeekSummaryFormat",
            Owner.Format("BuilderWorkouts", Days.Sum(d => d.Sessions.Count(s => s.IsWorkout))),
            Owner.Format("BuilderRestDays", Days.Count(d => d.IsRest)));

    public string AutomationId => $"BuilderWeek{Number}";

    public string CopyNextAutomationId => $"BuilderWeek{Number}CopyNext";

    public string CopyAllAutomationId => $"BuilderWeek{Number}CopyAll";

    public bool CanCopyToNext => Owner.HasWeekAfter(this);

    public bool CanCopyToAll => Owner.Weeks.Count > 1;

    [RelayCommand]
    private void CopyToNext() => Owner.CopyWeek(this, toAll: false);

    /// <summary>Overwrites every other week, so it asks first.</summary>
    [RelayCommand]
    private Task CopyToAllAsync() => Owner.CopyToAllAsync(this);

    [RelayCommand]
    private void Select() => Owner.SelectedWeek = this;

    internal IReadOnlyList<IReadOnlyList<CustomSession>> ToDays() => [.. Days.Select(d => d.ToSessions())];

    internal void Set(IReadOnlyList<IReadOnlyList<CustomSession>> days)
    {
        for (int d = 0; d < Days.Count; d++)
        {
            Days[d].Set(d < days.Count ? days[d] : []);
        }

        Changed();
    }

    internal void Changed()
    {
        OnPropertyChanged(nameof(EmptyDays));
        OnPropertyChanged(nameof(HasEmptyDays));
        OnPropertyChanged(nameof(Summary));
        Owner.Revalidate();
    }

    internal void RefreshCopyState()
    {
        OnPropertyChanged(nameof(CanCopyToNext));
        OnPropertyChanged(nameof(CanCopyToAll));
    }
}
