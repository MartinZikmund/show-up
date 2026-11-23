using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;

namespace ShowUp.Core.Tests.Packaging;

/// <summary>The store listing text in <c>store/listing</c>: the stores reject over-long fields, and the disclaimer is not optional.</summary>
[TestClass]
public class StoreListingTests
{
    // The tightest limit across Google Play, the App Store and the Microsoft Store, so one text fits all three.
    private const int MaxName = 30;
    private const int MaxShortDescription = 80;
    private const int MaxDescription = 4000;
    private const int MaxKeywords = 7;
    private const int MaxKeywordsLength = 100;

    private static readonly string[] _sections = ["Short description", "Description", "Keywords", "What's new"];

    [TestMethod]
    [DataRow("en-US", "en")]
    [DataRow("cs-CZ", "cs")]
    public void Listing_HasEverySection_WithinTheStoreLimits(string listing, string language)
    {
        var (name, sections) = Read(listing);

        name.Should().NotBeNullOrWhiteSpace();
        name.Length.Should().BeLessThanOrEqualTo(MaxName);
        sections.Keys.Should().BeEquivalentTo(_sections);
        sections.Values.Should().OnlyContain(text => text.Length > 0);
        sections["Short description"].Length.Should().BeLessThanOrEqualTo(MaxShortDescription);
        sections["Short description"].Should().NotContain("\n");
        sections["Description"].Length.Should().BeLessThanOrEqualTo(MaxDescription);

        var keywords = sections["Keywords"].Split(',', StringSplitOptions.TrimEntries);
        keywords.Should().HaveCountLessThanOrEqualTo(MaxKeywords).And.OnlyContain(k => k.Length > 0).And.OnlyHaveUniqueItems();
        sections["Keywords"].Length.Should().BeLessThanOrEqualTo(MaxKeywordsLength);
        _ = language;
    }

    [TestMethod]
    [DataRow("en-US", "en")]
    [DataRow("cs-CZ", "cs")]
    public void Listing_DescriptionEndsWithTheAppsOwnDisclaimer(string listing, string language)
    {
        var disclaimer = Resource(language, "AboutDisclaimer");

        Read(listing).Sections["Description"].Should().EndWith(disclaimer, "the listing must carry the same not-affiliated statement as the app");
    }

    [TestMethod]
    [DataRow("en-US")]
    [DataRow("cs-CZ")]
    public void Listing_NeverUsesAProgramNameAsAKeyword(string listing)
    {
        // Program names are other people's trademarks: fine to describe a schedule, not to rank on.
        string[] trademarks = ["insanity", "p90x", "beachbody", "bodi", "t25", "liift", "21 day fix"];

        var keywords = Read(listing).Sections["Keywords"].ToLowerInvariant();

        trademarks.Should().OnlyContain(mark => !keywords.Contains(mark));
    }

    private static (string Name, Dictionary<string, string> Sections) Read(string listing)
    {
        var text = File.ReadAllText(Path.Combine(FindRepoRoot(), "store", "listing", listing + ".md")).Replace("\r\n", "\n");
        var name = Regex.Match(text, @"\A# ([^\n]+)\n").Groups[1].Value.Trim();
        var sections = Regex.Matches(text, @"^## ([^\n]+)\n(.*?)(?=^## |\z)", RegexOptions.Multiline | RegexOptions.Singleline)
            .ToDictionary(m => m.Groups[1].Value.Trim(), m => m.Groups[2].Value.Trim());
        return (name, sections);
    }

    private static string Resource(string language, string key) =>
        XDocument.Load(Path.Combine(FindRepoRoot(), "src", "ShowUp", "Strings", language, "Resources.resw"))
            .Descendants("data").Single(d => (string?)d.Attribute("name") == key).Element("value")!.Value;

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
