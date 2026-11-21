using System.Globalization;

namespace ShowUp.Core.Localization;

public enum PluralCategory
{
    One,
    Few,
    Other,
}

/// <summary>Picks "1 day / 2 days"-style resource keys: <c>Key_One</c>, <c>Key_Few</c> or <c>Key_Other</c>.</summary>
public static class PluralForms
{
    public static PluralCategory CategoryFor(CultureInfo culture, int count)
    {
        if (count == 1)
        {
            return PluralCategory.One;
        }

        // Czech: 2-4 take the "few" form.
        return culture.TwoLetterISOLanguageName == "cs" && count is >= 2 and <= 4
            ? PluralCategory.Few
            : PluralCategory.Other;
    }

    public static string KeyFor(CultureInfo culture, string baseKey, int count) =>
        $"{baseKey}_{CategoryFor(culture, count)}";

    public static string Format(this IStringLocalizer localizer, string baseKey, int count) =>
        localizer[KeyFor(CultureInfo.CurrentUICulture, baseKey, count), count].Value;
}
