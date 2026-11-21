using System.Globalization;
using ShowUp.Core.Catalog;

namespace ShowUp.Core.Tracking;

/// <summary>How a logged value and its change read in the results table, in the user's unit system.</summary>
public static class MetricFormat
{
    public static string Value(MetricUnit unit, MetricValue value, UnitSystem system, CultureInfo? culture = null)
    {
        if (unit == MetricUnit.Text || value.Number is not { } number)
        {
            return value.Text ?? "";
        }

        return Number(unit, MetricUnits.ToDisplay(unit, number, system), culture ?? CultureInfo.CurrentCulture);
    }

    /// <summary>"+5", "-2.5", "+0:12"; empty when the change rounds to nothing.</summary>
    public static string Delta(MetricUnit unit, double delta, UnitSystem system, CultureInfo? culture = null)
    {
        var display = MetricUnits.ToDisplay(unit, delta, system);
        var magnitude = Number(unit, Math.Abs(display), culture ?? CultureInfo.CurrentCulture);
        if (magnitude is "0" or "0:00")
        {
            return "";
        }

        return (display > 0 ? "+" : "-") + magnitude;
    }

    private static string Number(MetricUnit unit, double number, CultureInfo culture) => unit switch
    {
        MetricUnit.Duration => DurationParser.Format(number),
        MetricUnit.Weight or MetricUnit.Distance => number.ToString("0.##", culture),
        _ => number.ToString("0", culture),
    };
}
