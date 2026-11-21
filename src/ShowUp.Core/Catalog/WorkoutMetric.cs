namespace ShowUp.Core.Catalog;

public enum MetricUnit
{
    Reps,
    Count,
    Seconds,

    /// <summary>A time entered as mm:ss, stored in seconds.</summary>
    Duration,
    Weight,
    Distance,
    Text,
}

/// <summary>A number a workout asks you to write down, such as a Fit Test move's reps or a MAX:30 Max Out time.</summary>
public sealed partial record WorkoutMetric(string Id, string Name, MetricUnit Unit, bool HigherIsBetter = true)
{
    public bool IsNumeric => Unit != MetricUnit.Text;
}
