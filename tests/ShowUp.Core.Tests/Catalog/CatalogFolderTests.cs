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

    [TestMethod]
    public void WorkoutIds_AreAppendOnly_MatchTheLockFile()
    {
        var dir = FindCatalogDir();
        var current = CatalogParser.ParseWorkouts(File.ReadAllText(Path.Combine(dir, "workouts.json"))).Select(w => w.Id).ToHashSet();
        var locked = File.ReadAllLines(Path.Combine(dir, "workout-ids.lock")).Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet();

        locked.Except(current).Should().BeEmpty("workout ids are never removed or renamed: custom programs and enrollments pin them");
        current.Except(locked).Should().BeEmpty("append new workout ids to catalog/workout-ids.lock (keep it sorted)");
    }

    [TestMethod]
    public void WorkoutIdsLock_IsSortedAndUnique()
    {
        var lines = File.ReadAllLines(Path.Combine(FindCatalogDir(), "workout-ids.lock")).Where(l => l.Trim().Length > 0).ToList();

        lines.Should().BeInAscendingOrder(StringComparer.Ordinal).And.OnlyHaveUniqueItems();
    }

    [TestMethod]
    public void EveryProgram_HasABundledHeroThatExists()
    {
        var catalogDir = FindCatalogDir();
        var catalog = LoadCatalog(catalogDir);

        foreach (var program in catalog.Programs)
        {
            program.HeroImage.Should().Be($"Assets/heroes/{program.Id}.jpg", $"{program.Id} needs hero art");
            File.Exists(Path.Combine(catalogDir, "heroes", $"{program.Id}.jpg")).Should().BeTrue($"catalog/heroes/{program.Id}.jpg must exist");
        }
    }

    [TestMethod]
    public void EveryHero_PassesHeroImagePolicy()
    {
        LoadCatalog(FindCatalogDir()).Programs.Should().OnlyContain(p => p.HeroImage != null && HeroImagePolicy.IsAllowed(p.HeroImage, null));
    }

    [TestMethod]
    public void EveryHero_IsAJpegOfAtMost150KB_AndBelongsToAProgram()
    {
        var catalogDir = FindCatalogDir();
        var ids = LoadCatalog(catalogDir).Programs.Select(p => p.Id).ToHashSet();
        var heroes = Directory.Exists(Path.Combine(catalogDir, "heroes")) ? Directory.GetFiles(Path.Combine(catalogDir, "heroes")) : [];

        heroes.Should().NotBeEmpty();
        foreach (var hero in heroes)
        {
            ids.Should().Contain(Path.GetFileNameWithoutExtension(hero), "a hero without a program is dead weight in every package");
            Path.GetExtension(hero).Should().Be(".jpg");
            new FileInfo(hero).Length.Should().BeLessThanOrEqualTo(150 * 1024, Path.GetFileName(hero));

            // JPEG files start with FF D8 FF.
            File.ReadAllBytes(hero).Take(3).Should().Equal((byte)0xFF, (byte)0xD8, (byte)0xFF);
        }
    }

    private static WorkoutCatalog LoadCatalog(string catalogDir) =>
        CatalogParser.Parse(
            1,
            File.ReadAllText(Path.Combine(catalogDir, "workouts.json")),
            Directory.GetFiles(Path.Combine(catalogDir, "programs"), "*.json").Select(File.ReadAllText));

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
