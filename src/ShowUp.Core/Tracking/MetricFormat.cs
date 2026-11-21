using System.Globalization;
using ShowUp.Core.Catalog;

namespace ShowUp.Core.Tracking;

/// <summary>How a logged value and its change read in the results table.</summary>
public static class MetricFormat
{
    public static string Value(MetricUnit unit, MetricValue value, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (unit == MetricUnit.Text || value.Number is not { } number)
        {
            return value.Text ?? "";
        }

        return unit switch
        {
            MetricUnit.Duration => DurationParser.Format(number),
            MetricUnit.Weight or MetricUnit.Distance => number.ToString("0.##", culture),
            _ => number.ToString("0", culture),
        };
    }

    /// <summary>"+5", "-2.5", "+0:12"; empty for no change.</summary>
    public static string Delta(MetricUnit unit, double delta, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (Math.Abs(delta) < 0.005)
        {
            return "";
        }

        var sign = delta > 0 ? "+" : "-";
        var magnitude = Math.Abs(delta);
        return sign + (unit switch
        {
            MetricUnit.Duration => DurationParser.Format(magnitude),
            MetricUnit.Weight or MetricUnit.Distance => magnitude.ToString("0.##", culture),
            _ => magnitude.ToString("0", culture),
        });
    }
}
