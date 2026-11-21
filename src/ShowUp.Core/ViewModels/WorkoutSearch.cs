using System.Globalization;
using System.Text;
using ShowUp.Core.Catalog;

namespace ShowUp.Core.ViewModels;

/// <summary>Name search over workouts, ignoring case and diacritics; prefix matches first. Rest is never offered.</summary>
public sealed class WorkoutSearch
{
    public const int MaxSuggestions = 20;

    private readonly IReadOnlyList<(Workout Workout, string Folded)> _workouts;

    public WorkoutSearch(IEnumerable<Workout> workouts)
    {
        ArgumentNullException.ThrowIfNull(workouts);
        _workouts =
        [
            .. workouts
                .Where(w => w.Kind != WorkoutKind.Rest)
                .OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(w => (w, Fold(w.Name))),
        ];
    }

    public IReadOnlyList<Workout> Search(string query)
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
