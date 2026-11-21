using ShowUp.Core.Catalog;

namespace ShowUp.Core.ViewModels;

/// <summary>One session of today's schedule, with the button that crosses it off.</summary>
public sealed partial class TodaySessionViewModel : ObservableObject
{
    public TodaySessionViewModel(
        int index,
        string name,
        int? durationMinutes,
        WorkoutKind kind,
        string kindLabel,
        string durationLabel,
        bool isOptional,
        bool isDone,
        Func<TodaySessionViewModel, Task> cross)
    {
        Index = index;
        Name = name;
        DurationMinutes = durationMinutes;
        Kind = kind;
        KindLabel = kindLabel;
        DurationLabel = durationLabel;
        IsOptional = isOptional;
        IsDone = isDone;
        CrossCommand = new AsyncRelayCommand(() => cross(this));
    }

    public int Index { get; }

    public string Name { get; }

    public int? DurationMinutes { get; }

    public WorkoutKind Kind { get; }

    public string KindLabel { get; }

    /// <summary>"30 min", or empty when the workout has no set length.</summary>
    public string DurationLabel { get; }

    public bool IsOptional { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    public partial bool IsDone { get; set; }

    public bool IsOpen => !IsDone;

    public IAsyncRelayCommand CrossCommand { get; }
}
