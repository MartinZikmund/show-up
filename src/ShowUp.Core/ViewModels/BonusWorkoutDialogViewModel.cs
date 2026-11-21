using System.Globalization;
using System.Text;
using ShowUp.Core.Catalog;
using ShowUp.Core.Services;

namespace ShowUp.Core.ViewModels;

/// <summary>State of the bonus workout dialog: search the catalog or type a title, set a duration and a day (today or earlier).</summary>
public sealed partial class BonusWorkoutDialogViewModel : ObservableObject
{
    public const int MaxSuggestions = 20;
    public const int MaxTitleLength = 80;
    public const int MaxDurationMinutes = 600;

    private readonly IReadOnlyList<(Workout Workout, string Folded)> _workouts;

    public BonusWorkoutDialogViewModel(IEnumerable<Workout> workouts, DateOnly today, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(workouts);
        _workouts =
        [
            .. workouts
                .Where(w => w.Kind != WorkoutKind.Rest)
                .OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(w => (w, Fold(w.Name))),
        ];
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

        Suggestions = Search(value);
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

    private List<Workout> Search(string query)
    {
        var needle = Fold(query.Trim());
        if (needle.Length == 0)
        {
            return [];
        }

        return
        [
            .. _workouts
                .Where(w => w.Folded.Contains(needle, StringComparison.Ordinal))
                .OrderByDescending(w => w.Folded.StartsWith(needle, StringComparison.Ordinal))
                .Select(w => w.Workout)
                .Take(MaxSuggestions),
        ];
    }

    // Case and diacritics don't matter: "cardio" finds "Cardío", "ab" finds "Ab Attack".
    private static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}
