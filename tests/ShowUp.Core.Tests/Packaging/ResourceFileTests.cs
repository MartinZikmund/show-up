using System.Xml.Linq;
using AwesomeAssertions;

namespace ShowUp.Core.Tests.Packaging;

[TestClass]
public class ResourceFileTests
{
    [TestMethod]
    public void Czech_HasEveryEnglishKey()
    {
        Load("cs").Keys.Should().BeEquivalentTo(Load("en").Keys);
    }

    [TestMethod]
    public void Czech_FitTest_IsTranslated()
    {
        Load("cs")["WorkoutKind_FitTest"].Should().Be("Test kondice");
    }

    [TestMethod]
    public void BonusCount_HasAllThreePluralForms()
    {
        foreach (var language in new[] { "en", "cs" })
        {
            Load(language).Keys.Where(k => k.StartsWith("CalendarDayBonus", StringComparison.Ordinal))
                .Should().BeEquivalentTo("CalendarDayBonus_One", "CalendarDayBonus_Few", "CalendarDayBonus_Other");
        }
    }

    private static Dictionary<string, string> Load(string language)
    {
        var path = Path.Combine(FindRepoRoot(), "src", "ShowUp", "Strings", language, "Resources.resw");
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string)e.Element("value")!);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "version.json")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root (version.json) not found.");
    }
}
