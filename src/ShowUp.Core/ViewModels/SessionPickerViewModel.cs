using ShowUp.Core.Catalog;
using ShowUp.Core.Services;

namespace ShowUp.Core.ViewModels;

/// <summary>State of the session picker: pick a workout from the search, or type a title to make one (with minutes and a kind).</summary>
public sealed partial class SessionPickerViewModel : ObservableObject
{
    public const int MaxTitleLength = BonusWorkoutDialogViewModel.MaxTitleLength;
    public const int MaxDurationMinutes = BonusWorkoutDialogViewModel.MaxDurationMinutes;

    private readonly WorkoutSearch _search;

    public SessionPickerViewModel(IEnumerable<Workout> workouts) => _search = new(workouts);

    /// <summary>Kinds a custom workout can have; rest days and fit tests come from the catalog.</summary>
    public static IReadOnlyList<WorkoutKind> CustomKinds { get; } =
        [WorkoutKind.Cardio, WorkoutKind.Strength, WorkoutKind.Abs, WorkoutKind.Yoga, WorkoutKind.Stretch, WorkoutKind.Recovery, WorkoutKind.Mixed];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustom), nameof(Title), nameof(CanSave))]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<Workout> Suggestions { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustom), nameof(Title), nameof(CanSave))]
    public partial Workout? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial int? DurationMinutes { get; set; }

    [ObservableProperty]
    public partial WorkoutKind Kind { get; set; } = WorkoutKind.Cardio;

    [ObservableProperty]
    public partial bool Optional { get; set; }

    /// <summary>A typed title with nothing picked: the user's own workout, so kind and minutes apply.</summary>
    public bool IsCustom => Selected is null && Query.Trim().Length > 0;

    public string Title => Selected?.Name ?? Query.Trim();

    public bool CanSave => Title.Length > 0 && (Selected is not null || DurationMinutes is null or (> 0 and <= MaxDurationMinutes));

    partial void OnQueryChanged(string value)
    {
        if (value.Length > MaxTitleLength)
        {
            Query = value[..MaxTitleLength];
            return;
        }

        if (Selected is { } selected && !string.Equals(selected.Name, value, StringComparison.Ordinal))
        {
            Selected = null;
        }

        Suggestions = _search.Search(value);
    }

    partial void OnSelectedChanged(Workout? value)
    {
        if (value is not null)
        {
            Query = value.Name;
            DurationMinutes = value.DurationMinutes;
        }
    }

    public SessionPick ToPick() =>
        CanSave
            ? new(Selected, Title, Selected?.DurationMinutes ?? DurationMinutes, Selected?.Kind ?? Kind, Optional)
            : throw new InvalidOperationException("Pick or name a workout first.");
}
