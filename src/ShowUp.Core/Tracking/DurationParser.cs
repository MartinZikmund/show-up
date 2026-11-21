using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ShowUp.Core.Tracking;

/// <summary>Durations typed as <c>mm:ss</c> (minutes may be any length, seconds two digits), stored as whole seconds.</summary>
public static class DurationParser
{
    public const int MaxMinutes = 999;

    public static bool TryParse([NotNullWhen(true)] string? text, out int seconds)
    {
        seconds = 0;
        var parts = text?.Trim().Split(':');
        if (parts is not [var m, var s] || m.Length is 0 or > 3 || s.Length != 2 || !m.All(char.IsAsciiDigit) || !s.All(char.IsAsciiDigit))
        {
            return false;
        }

        var minutes = int.Parse(m, CultureInfo.InvariantCulture);
        var secs = int.Parse(s, CultureInfo.InvariantCulture);
        if (secs > 59 || minutes > MaxMinutes)
        {
            return false;
        }

        seconds = minutes * 60 + secs;
        return true;
    }

    /// <summary>12:05, or -0:30 for a negative delta.</summary>
    public static string Format(double seconds)
    {
        var total = (int)Math.Round(Math.Abs(seconds));
        var sign = seconds < 0 && total > 0 ? "-" : "";
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{total / 60}:{total % 60:00}");
    }
}
