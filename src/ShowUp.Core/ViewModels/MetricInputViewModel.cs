using ShowUp.Core.Catalog;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.ViewModels;

/// <summary>
/// One metric to fill in. Reps, counts and seconds take whole numbers, weight and distance decimals in the user's units
/// (NumberBox, NaN = empty); a duration is typed as mm:ss and a text metric as free text (TextBox).
/// </summary>
public sealed partial class MetricInputViewModel : ObservableObject
{
    public const double MaxNumber = 100_000;

    private readonly MetricValue _initial;
    private readonly double _initialNumber = double.NaN;
    private readonly string _initialText = "";
    private readonly UnitSystem _system;

    public MetricInputViewModel(WorkoutMetric metric, MetricValue? initial, IStringLocalizer localizer, UnitSystem system)
    {
        ArgumentNullException.ThrowIfNull(metric);
        ArgumentNullException.ThrowIfNull(localizer);
        Metric = metric;
        _system = system;
        _initial = initial ?? default;
        UnitHint = MetricUnits.Label(metric.Unit, system, localizer);
        var hint = metric.Unit == MetricUnit.Duration ? PlaceholderText : UnitHint;
        AccessibleName = hint.Length == 0 ? metric.Name : $"{metric.Name} ({hint})";
        DurationError = localizer["MetricDurationError"];

        if (IsNumberInput)
        {
            // Shown rounded; an untouched box gives back the stored value at full precision.
            Number = _initialNumber = _initial.Number is { } stored ? Math.Round(MetricUnits.ToDisplay(metric.Unit, stored, system), 2) : double.NaN;
        }
        else
        {
            Text = _initialText = metric.Unit == MetricUnit.Duration && _initial.Number is { } seconds ? DurationParser.Format(seconds) : _initial.Text ?? "";
        }
    }

    public WorkoutMetric Metric { get; }

    public string Name => Metric.Name;

    /// <summary>The metric's name with its unit, for screen readers.</summary>
    public string AccessibleName { get; }

    public string UnitHint { get; }

    public string AutomationId => $"MetricInput_{Metric.Id}";

    // Only the visible box carries the id, so UI tests find exactly one.
    public string NumberAutomationId => IsNumberInput ? AutomationId : "";

    public string TextAutomationId => IsTextInput ? AutomationId : "";

    public bool IsNumberInput => Metric.Unit is not (MetricUnit.Duration or MetricUnit.Text);

    public bool IsTextInput => !IsNumberInput;

    public bool IsWholeNumber => Metric.Unit is MetricUnit.Reps or MetricUnit.Count or MetricUnit.Seconds;

    /// <summary>For the NumberBox: one decimal place or none.</summary>
    public double SmallChange => IsWholeNumber ? 1 : 0.5;

    public string PlaceholderText => Metric.Unit == MetricUnit.Duration ? "mm:ss" : "";

    public string DurationError { get; }

    /// <summary>The error while there is one, for the box's help text.</summary>
    public string ErrorText => ShowError ? DurationError : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid), nameof(IsChanged))]
    public partial double Number { get; set; } = double.NaN;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid), nameof(IsChanged), nameof(ShowError), nameof(ErrorText))]
    public partial string Text { get; set; } = "";

    public bool IsValid => IsNumberInput
        ? double.IsNaN(Number) || Number is >= 0 and <= MaxNumber
        : Metric.Unit != MetricUnit.Duration || string.IsNullOrWhiteSpace(Text) || DurationParser.TryParse(Text, out _);

    /// <summary>A typed duration that isn't mm:ss.</summary>
    public bool ShowError => !IsValid && IsTextInput;

    public bool IsChanged => IsValid && (IsNumberInput
        ? !(double.IsNaN(Number) && double.IsNaN(_initialNumber)) && Number != _initialNumber
        : Text.Trim() != _initialText);

    /// <summary>What to store: SI units at full precision for weight and distance.</summary>
    public MetricValue Value
    {
        get
        {
            if (!IsChanged)
            {
                return _initial;
            }

            if (IsNumberInput)
            {
                return double.IsNaN(Number) ? default : new(MetricUnits.FromDisplay(Metric.Unit, Number, _system));
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
}

/// <summary>The inputs of one <see cref="ResultsSheet"/>, valid when every input is.</summary>
public sealed class ResultsEditorViewModel
{
    public ResultsEditorViewModel(ResultsSheet sheet, IStringLocalizer localizer, UnitSystem system)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        Sheet = sheet;
        Inputs = [.. (sheet.Workout.Metrics ?? []).Select(m => new MetricInputViewModel(m, sheet.Values.TryGetValue(m.Id, out var v) ? v : null, localizer, system))];
    }

    public ResultsSheet Sheet { get; }

    public IReadOnlyList<MetricInputViewModel> Inputs { get; }

    public bool IsValid => Inputs.All(i => i.IsValid);

    public IReadOnlyDictionary<string, MetricValue> Changes => Inputs.Where(i => i.IsChanged).ToDictionary(i => i.Metric.Id, i => i.Value);
}
