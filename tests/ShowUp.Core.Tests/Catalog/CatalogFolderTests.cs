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
