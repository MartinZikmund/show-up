using ShowUp.Core.Catalog;
using ShowUp.Core.Services;

namespace ShowUp.Core.ViewModels;

/// <summary>State of the bonus workout dialog: search the catalog or type a title, set a duration and a day (today or earlier).</summary>
public sealed partial class BonusWorkoutDialogViewModel : ObservableObject
{
    public const int MaxSuggestions = WorkoutSearch.MaxSuggestions;
    public const int MaxTitleLength = 80;
    public const int MaxDurationMinutes = 600;

    private readonly WorkoutSearch _search;

    public BonusWorkoutDialogViewModel(IEnumerable<Workout> workouts, DateOnly today, DateOnly date)
    {
        _search = new(workouts);
        Today = today;
        Date = date > today ? today : date;
    }

    public DateOnly Today { get; }

    /// <summary>The box's text: a catalog workout's name once picked, otherwise the custom title.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomTitle), nameof(Title), nameof(CanSave))]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<Workout> Suggestions { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomTitle), nameof(Title), nameof(CanSave))]
    public partial Workout? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial int? DurationMinutes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial DateOnly Date { get; set; }

    /// <summary>The typed title when no catalog workout is picked.</summary>
    public string CustomTitle => Selected is null ? Query.Trim() : "";

    public string Title => Selected?.Name ?? CustomTitle;

    public bool CanSave => Title.Length > 0 && Date <= Today && DurationMinutes is null or (> 0 and <= MaxDurationMinutes);

    /// <summary>A cleared date picker hands back null; the day falls back to today.</summary>
    public void SetDate(DateOnly? date) => Date = date is { } d && d <= Today ? d : Today;

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
        if (value is null)
        {
            return;
        }

        Query = value.Name;
        DurationMinutes = value.DurationMinutes;
    }

    public BonusWorkoutEntry ToEntry() =>
        CanSave
            ? new(Selected?.Id, Title, DurationMinutes, Date)
            : throw new InvalidOperationException("The bonus workout isn't valid yet.");
}
