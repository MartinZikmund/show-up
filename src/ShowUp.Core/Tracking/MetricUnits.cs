using System.Globalization;
using ShowUp.Core.Catalog;

namespace ShowUp.Core.Tracking;

public enum UnitSystem
{
    Metric,
    Imperial,
}

/// <summary>
/// Weight and distance are stored in kg and km at full precision; only what the user sees and types is in their unit system.
/// </summary>
public static class MetricUnits
{
    public const double KilogramsPerPound = 0.45359237;
    public const double KilometersPerMile = 1.609344;

    public static double ToDisplay(MetricUnit unit, double stored, UnitSystem system) => (unit, system) switch
    {
        (MetricUnit.Weight, UnitSystem.Imperial) => stored / KilogramsPerPound,
        (MetricUnit.Distance, UnitSystem.Imperial) => stored / KilometersPerMile,
        _ => stored,
    };

    public static double FromDisplay(MetricUnit unit, double display, UnitSystem system) => (unit, system) switch
    {
        (MetricUnit.Weight, UnitSystem.Imperial) => display * KilogramsPerPound,
        (MetricUnit.Distance, UnitSystem.Imperial) => display * KilometersPerMile,
        _ => display,
    };

    /// <summary>The resource key of the unit's short label, or null when it needs none (durations show mm:ss, text nothing).</summary>
    public static string? LabelKey(MetricUnit unit, UnitSystem system) => unit switch
    {
        MetricUnit.Reps => "MetricUnitReps",
        MetricUnit.Count => "MetricUnitCount",
        MetricUnit.Seconds => "MetricUnitSeconds",
        MetricUnit.Weight => system == UnitSystem.Imperial ? "MetricUnitLb" : "MetricUnitKg",
        MetricUnit.Distance => system == UnitSystem.Imperial ? "MetricUnitMi" : "MetricUnitKm",
        _ => null,
    };

    public static string Label(MetricUnit unit, UnitSystem system, IStringLocalizer localizer) =>
        LabelKey(unit, system) is { } key ? localizer[key].Value : "";

    /// <summary>The region's system, used once to seed the preference.</summary>
    public static UnitSystem FromRegion()
    {
        try
        {
            return RegionInfo.CurrentRegion.IsMetric ? UnitSystem.Metric : UnitSystem.Imperial;
        }
        catch (ArgumentException)
        {
            return UnitSystem.Metric;
        }
    }
}
