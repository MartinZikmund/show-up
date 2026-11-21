using System.Globalization;
using ShowUp.Core.Catalog;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.ViewModels;

/// <summary>
/// One metric to fill in. Reps, counts and seconds take whole numbers, weight and distance decimals (NumberBox, NaN = empty);
/// a duration is typed as mm:ss and a text metric as free text (TextBox).
/// </summary>
public sealed partial class MetricInputViewModel : ObservableObject
{
    public const double MaxNumber = 100_000;

    private readonly MetricValue _initial;

    public MetricInputViewModel(WorkoutMetric metric, MetricValue? initial, IStringLocalizer localizer, bool usesMetricSystem)
    {
        ArgumentNullException.ThrowIfNull(metric);
        ArgumentNullException.ThrowIfNull(localizer);
        Metric = metric;
        _initial = initial ?? default;
        UnitHint = metric.Unit switch
        {
            MetricUnit.Reps => localizer["MetricUnitReps"],
            MetricUnit.Count => localizer["MetricUnitCount"],
            MetricUnit.Seconds => localizer["MetricUnitSeconds"],
            MetricUnit.Duration => localizer["MetricUnitDuration"],
            MetricUnit.Weight => localizer[usesMetricSystem ? "MetricUnitKg" : "MetricUnitLb"],
            MetricUnit.Distance => localizer[usesMetricSystem ? "MetricUnitKm" : "MetricUnitMi"],
            _ => "",
        };

        if (IsNumberInput)
        {
            Number = _initial.Number ?? double.NaN;
        }
        else
        {
            Text = metric.Unit == MetricUnit.Duration && _initial.Number is { } seconds ? DurationParser.Format(seconds) : _initial.Text ?? "";
        }
    }

    public WorkoutMetric Metric { get; }

    public string Name => Metric.Name;

    public string UnitHint { get; }

    public string AutomationId => $"MetricInput_{Metric.Id}";

    public bool IsNumberInput => Metric.Unit is not (MetricUnit.Duration or MetricUnit.Text);

    public bool IsTextInput => !IsNumberInput;

    public bool IsWholeNumber => Metric.Unit is MetricUnit.Reps or MetricUnit.Count or MetricUnit.Seconds;

    /// <summary>For the NumberBox: one decimal place or none.</summary>
    public double SmallChange => IsWholeNumber ? 1 : 0.5;

    public string PlaceholderText => Metric.Unit == MetricUnit.Duration ? "mm:ss" : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid), nameof(IsChanged))]
    public partial double Number { get; set; } = double.NaN;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid), nameof(IsChanged), nameof(ShowError))]
    public partial string Text { get; set; } = "";

    public bool IsValid => IsNumberInput
        ? double.IsNaN(Number) || Number is >= 0 and <= MaxNumber
        : Metric.Unit != MetricUnit.Duration || string.IsNullOrWhiteSpace(Text) || DurationParser.TryParse(Text, out _);

    /// <summary>A typed duration that isn't mm:ss.</summary>
    public bool ShowError => !IsValid && IsTextInput;

    public bool IsChanged => IsValid && Value != _initial;

    public MetricValue Value
    {
        get
        {
            if (IsNumberInput)
            {
                return double.IsNaN(Number) ? default : new(IsWholeNumber ? Math.Round(Number) : Math.Round(Number, 2));
            }

            if (Metric.Unit == MetricUnit.Duration)
            {
                return DurationParser.TryParse(Text, out var seconds) ? new(seconds) : default;
            }

            return string.IsNullOrWhiteSpace(Text) ? default : new(null, Text.Trim());
        }
    }

    partial void OnNumberChanged(double value)
    {
        if (IsWholeNumber && double.IsFinite(value) && value != Math.Round(value))
        {
            Number = Math.Round(value);
        }
    }

    public static bool UsesMetricSystem
    {
        get
        {
            try
            {
                return RegionInfo.CurrentRegion.IsMetric;
            }
            catch (ArgumentException)
            {
                return true;
            }
        }
    }
}

/// <summary>The inputs of one <see cref="ResultsSheet"/>, valid when every input is.</summary>
public sealed class ResultsEditorViewModel
{
    public ResultsEditorViewModel(ResultsSheet sheet, IStringLocalizer localizer, bool usesMetricSystem)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        Sheet = sheet;
        Inputs = [.. (sheet.Workout.Metrics ?? []).Select(m => new MetricInputViewModel(m, sheet.Values.TryGetValue(m.Id, out var v) ? v : null, localizer, usesMetricSystem))];
    }

    public ResultsSheet Sheet { get; }

    public IReadOnlyList<MetricInputViewModel> Inputs { get; }

    public bool IsValid => Inputs.All(i => i.IsValid);

    public IReadOnlyDictionary<string, MetricValue> Changes => Inputs.Where(i => i.IsChanged).ToDictionary(i => i.Metric.Id, i => i.Value);
}
