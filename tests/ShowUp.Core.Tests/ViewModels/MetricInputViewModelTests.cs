using AwesomeAssertions;
using ShowUp.Core.Catalog;
using ShowUp.Core.Tracking;
using ShowUp.Core.ViewModels;

namespace ShowUp.Core.Tests.ViewModels;

[TestClass]
public sealed class MetricInputViewModelTests
{
    [TestMethod]
    public void UnitHint_Duration_IsLeftToThePlaceholder()
    {
        var input = Create(MetricUnit.Duration);

        input.UnitHint.Should().BeEmpty();
        input.PlaceholderText.Should().Be("mm:ss");
    }

    [TestMethod]
    [DataRow(true, "MetricUnitKg")]
    [DataRow(false, "MetricUnitLb")]
    public void UnitHint_Weight_FollowsTheMeasurementSystem(bool metric, string expected) =>
        Create(MetricUnit.Weight, system: metric ? UnitSystem.Metric : UnitSystem.Imperial).UnitHint.Should().Be(expected);

    [TestMethod]
    public void Number_Reps_RoundsToAWholeNumber()
    {
        var input = Create(MetricUnit.Reps);

        input.Number = 12.6;

        input.Number.Should().Be(13);
        input.Value.Should().Be(new MetricValue(13));
    }

    [TestMethod]
    public void Number_Weight_KeepsDecimals()
    {
        var input = Create(MetricUnit.Weight);

        input.Number = 12.5;

        input.Value.Should().Be(new MetricValue(12.5));
    }

    [TestMethod]
    public void IsChanged_ClearedValue_WritesAnEmptyValue()
    {
        var input = Create(MetricUnit.Reps, new MetricValue(30));

        input.Number = double.NaN;

        input.IsChanged.Should().BeTrue();
        input.Value.IsEmpty.Should().BeTrue();
    }

    [TestMethod]
    public void Text_Duration_PrefilledAsMinutesAndSeconds() =>
        Create(MetricUnit.Duration, new MetricValue(754)).Text.Should().Be("12:34");

    [TestMethod]
    [DataRow(MetricUnit.Reps, "MetricInput_m", "")]
    [DataRow(MetricUnit.Duration, "", "MetricInput_m")]
    public void AutomationIds_OnlyTheVisibleBoxHasOne(MetricUnit unit, string number, string text)
    {
        var input = Create(unit);

        input.NumberAutomationId.Should().Be(number);
        input.TextAutomationId.Should().Be(text);
    }

    [TestMethod]
    public void Value_PoundsTyped_StoredAsKilogramsAndShownBackAsPounds()
    {
        var typed = Create(MetricUnit.Weight, system: UnitSystem.Imperial);

        typed.Number = 25;
        var stored = typed.Value;

        stored.Number.Should().BeApproximately(11.33980925, 1e-9);
        Create(MetricUnit.Weight, stored, UnitSystem.Imperial).Number.Should().Be(25);
    }

    [TestMethod]
    public void Value_UnitSystemChanged_StoredMeaningStays()
    {
        MetricValue stored = new(12.5);

        var metric = Create(MetricUnit.Weight, stored, UnitSystem.Metric);
        var imperial = Create(MetricUnit.Weight, stored, UnitSystem.Imperial);

        metric.Number.Should().Be(12.5);
        imperial.Number.Should().Be(27.56);
        imperial.IsChanged.Should().BeFalse();
        imperial.Value.Should().Be(stored, "an untouched box gives back the exact stored kilograms");
    }

    [TestMethod]
    public void Value_Distance_MilesStoredAsKilometers()
    {
        var input = Create(MetricUnit.Distance, system: UnitSystem.Imperial);

        input.Number = 3.1;

        input.Value.Number.Should().BeApproximately(4.9889664, 1e-7);
    }

    [TestMethod]
    [DataRow(MetricUnit.Reps, "Metric (MetricUnitReps)")]
    [DataRow(MetricUnit.Duration, "Metric (mm:ss)")]
    [DataRow(MetricUnit.Text, "Metric")]
    public void AccessibleName_IncludesTheUnit(MetricUnit unit, string expected) =>
        Create(unit).AccessibleName.Should().Be(expected);

    [TestMethod]
    public void ErrorText_InvalidDuration_IsSetForScreenReaders()
    {
        var input = Create(MetricUnit.Duration);

        input.Text = "90";

        input.ErrorText.Should().Be("MetricDurationError");
        input.Text = "1:30";
        input.ErrorText.Should().BeEmpty();
    }

    private static MetricInputViewModel Create(MetricUnit unit, MetricValue? initial = null, UnitSystem system = UnitSystem.Metric) =>
        new(new WorkoutMetric("m", "Metric", unit), initial, new FakeStringLocalizer(), system);
}
