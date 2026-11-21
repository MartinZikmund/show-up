using System.Globalization;
using AwesomeAssertions;
using ShowUp.Core.Catalog;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Tracking;

[TestClass]
public sealed class MetricFormatTests
{
    [TestMethod]
    [DataRow(UnitSystem.Metric, "12.5")]
    [DataRow(UnitSystem.Imperial, "27.56")]
    public void Value_Weight_ShownInTheUnitSystem(UnitSystem system, string expected) =>
        MetricFormat.Value(MetricUnit.Weight, new MetricValue(12.5), system, CultureInfo.InvariantCulture).Should().Be(expected);

    [TestMethod]
    public void Value_Duration_ReadsAsMinutesAndSeconds() =>
        MetricFormat.Value(MetricUnit.Duration, new MetricValue(754), UnitSystem.Metric).Should().Be("12:34");

    [TestMethod]
    [DataRow(2.0, "+2")]
    [DataRow(-3.0, "-3")]
    [DataRow(0.2, "")]
    public void Delta_Reps_SignedAndEmptyWhenItRoundsToNothing(double delta, string expected) =>
        MetricFormat.Delta(MetricUnit.Reps, delta, UnitSystem.Metric, CultureInfo.InvariantCulture).Should().Be(expected);

    [TestMethod]
    public void Delta_Weight_ConvertedToPounds() =>
        MetricFormat.Delta(MetricUnit.Weight, 1, UnitSystem.Imperial, CultureInfo.InvariantCulture).Should().Be("+2.2");
}
