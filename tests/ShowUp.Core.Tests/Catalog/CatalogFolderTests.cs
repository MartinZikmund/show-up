using AwesomeAssertions;
using ShowUp.Core.Catalog;

namespace ShowUp.Core.Tests.Catalog;

[TestClass]
public class CatalogFolderTests
{
    [TestMethod]
    public void CatalogFolder_AllFiles_AreValid()
    {
        var catalogDir = FindCatalogDir();
        var programs = Directory.GetFiles(Path.Combine(catalogDir, "programs"), "*.json").Select(File.ReadAllText);

        var catalog = CatalogParser.Parse(1, File.ReadAllText(Path.Combine(catalogDir, "workouts.json")), programs);

        CatalogValidator.Validate(catalog).Should().BeEmpty();
    }

    [TestMethod]
    public void CatalogIndex_MatchesProgramFiles()
    {
        var catalogDir = FindCatalogDir();
        var indexed = CatalogParser.ParseProgramIndex(File.ReadAllText(Path.Combine(catalogDir, "index.json")));
        var files = Directory.GetFiles(Path.Combine(catalogDir, "programs"), "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal);

        indexed.Should().Equal(files);
    }

    [TestMethod]
    public void CatalogFolder_FitTestsAndMaxOut_HaveVerifiedMetrics()
    {
        var workouts = CatalogParser.ParseWorkouts(File.ReadAllText(Path.Combine(FindCatalogDir(), "workouts.json"))).ToDictionary(w => w.Id);

        workouts["insanity-fit-test"].Metrics.Should().HaveCount(8).And.OnlyContain(m => m.Unit == MetricUnit.Reps);
        workouts.Values.Where(w => w.Family == "Insanity MAX:30" && w.HasMetrics).Should().HaveCount(10)
            .And.OnlyContain(w => w.Metrics!.Single().Unit == MetricUnit.Duration);
        workouts["insanity-asylum-2-championship-fit-test"].Metrics.Should().ContainSingle().Which.Unit.Should().Be(MetricUnit.Count);
        workouts["insanity-unhinged-fit-test"].Metrics!.Count(m => m.Unit == MetricUnit.Weight).Should().Be(2);
    }

    private static string FindCatalogDir()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "catalog");
            if (File.Exists(Path.Combine(candidate, "workouts.json")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("catalog/ not found above " + AppContext.BaseDirectory);
    }
}
