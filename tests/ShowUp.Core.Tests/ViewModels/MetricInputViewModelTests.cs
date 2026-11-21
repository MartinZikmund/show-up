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

    private static MetricInputViewModel Create(MetricUnit unit, MetricValue? initial = null, UnitSystem system = UnitSystem.Metric) =>
        new(new WorkoutMetric("m", "Metric", unit), initial, new FakeStringLocalizer(), system);
}
