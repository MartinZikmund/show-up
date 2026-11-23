using AwesomeAssertions;
using PublishCatalog;

namespace ShowUp.Core.Tests.Publishing;

[TestClass]
public class CatalogLoaderTests
{
    private const string Url = "https://abc.supabase.co";

    [TestMethod]
    public void Load_RealCatalogFolder_IsValidAndMatchesTheLock()
    {
        var local = CatalogLoader.Load(FindCatalogDir(), Url);

        local.Errors.Should().BeEmpty();
        local.Programs.Should().NotBeEmpty();
        local.Workouts.Select(w => w.Id).Should().BeEquivalentTo(local.LockedWorkoutIds);
        // Every program ships bundled hero art, which a publish uploads under a content-hashed name.
        local.Programs.Should().OnlyContain(p => p.Hero != null && File.Exists(p.Hero.FilePath) && p.Hero.PublicUrl.EndsWith(".jpg", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Load_HeroInHeroesFolder_RewritesPublishedJsonToTheStorageUrl()
    {
        using TempCatalog temp = new("Assets/heroes/t.webp");
        File.WriteAllBytes(Path.Combine(temp.Dir, "heroes", "t.webp"), [1, 2, 3]);

        var local = CatalogLoader.Load(temp.Dir, Url);

        local.Errors.Should().BeEmpty();
        var program = local.Programs.Single();
        program.Hero.Should().NotBeNull();
        program.Hero!.ObjectName.Should().MatchRegex("^t-[0-9a-f]{12}\\.webp$");
        program.Json.GetProperty("heroImage").GetString().Should().Be(program.Hero.PublicUrl)
            .And.StartWith($"{Url}/storage/v1/object/public/catalog-heroes/");
        File.ReadAllText(Path.Combine(temp.Dir, "programs", "t.json")).Should().Contain("Assets/heroes/t.webp");
    }

    [TestMethod]
    public void Load_HeroWithoutFile_KeepsTheBundledPath()
    {
        using TempCatalog temp = new("Assets/heroes/t.webp");

        var local = CatalogLoader.Load(temp.Dir, Url);

        local.Programs.Single().Hero.Should().BeNull();
        local.Programs.Single().Json.GetProperty("heroImage").GetString().Should().Be("Assets/heroes/t.webp");
    }

    [TestMethod]
    public void Load_UnsupportedHeroType_IsAnError()
    {
        using TempCatalog temp = new("Assets/heroes/t.gif");
        File.WriteAllBytes(Path.Combine(temp.Dir, "heroes", "t.gif"), [1]);

        CatalogLoader.Load(temp.Dir, Url).Errors.Should().ContainSingle().Which.Should().Contain(".webp");
    }

    [TestMethod]
    public void Load_HeroOverTheBucketLimit_IsAnError()
    {
        using TempCatalog temp = new("Assets/heroes/t.png");
        File.WriteAllBytes(Path.Combine(temp.Dir, "heroes", "t.png"), new byte[CatalogLoader.MaxHeroBytes + 1]);

        CatalogLoader.Load(temp.Dir, Url).Errors.Should().ContainSingle().Which.Should().Contain("MiB");
    }

    [TestMethod]
    public void Load_ProgramUsingUnknownWorkout_ReportsTheValidatorError()
    {
        using TempCatalog temp = new(null, workoutId: "other");

        CatalogLoader.Load(temp.Dir, Url).Errors.Should().Contain(e => e.Contains("unknown workout 'w'"));
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

    private sealed class TempCatalog : IDisposable
    {
        public TempCatalog(string? heroImage, string workoutId = "w")
        {
            Dir = Path.Combine(Path.GetTempPath(), "publish-catalog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Dir, "programs"));
            Directory.CreateDirectory(Path.Combine(Dir, "heroes"));
            File.WriteAllText(Path.Combine(Dir, "workouts.json"),
                $$"""[{"id":"{{workoutId}}","name":"W","durationMinutes":null,"kind":"cardio","family":null}]""");
            var hero = heroImage is null ? "null" : $"\"{heroImage}\"";
            File.WriteAllText(Path.Combine(Dir, "programs", "t.json"), $$"""
                {"id":"t","revision":1,"name":"T","family":"F","description":"D","heroImage":{{hero}},"accentColor":"#FFD400",
                 "verification":"official","sources":[{"url":"https://example.com","confirms":"x"}],"verifiedOn":"2026-11-21",
                 "tracks":[{"id":"main","name":"Main","phases":[{"name":"P","weeks":[{"days":[{"sessions":[{"workoutId":"w"}]}]}]}]}]}
                """);
        }

        public string Dir { get; }

        public void Dispose() => Directory.Delete(Dir, recursive: true);
    }
}
