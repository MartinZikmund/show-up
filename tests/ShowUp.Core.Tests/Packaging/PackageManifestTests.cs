using System.Xml.Linq;
using AwesomeAssertions;

namespace ShowUp.Core.Tests.Packaging;

[TestClass]
public class PackageManifestTests
{
    private static readonly XNamespace _foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace _uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

    [TestMethod]
    public void VisualElements_Always_HasTransparentBackgroundColor()
    {
        var manifest = XDocument.Load(Path.Combine(FindRepoRoot(), "src", "ShowUp", "Package.appxmanifest"));

        var visualElements = manifest.Descendants(_uap + "VisualElements").Single();

        ((string?)visualElements.Attribute("BackgroundColor")).Should().Be("transparent");
    }

    [TestMethod]
    public void Identity_Always_HasPlaceholderVersion()
    {
        var manifest = XDocument.Load(Path.Combine(FindRepoRoot(), "src", "ShowUp", "Package.appxmanifest"));

        var identity = manifest.Descendants(_foundation + "Identity").Single();

        ((string?)identity.Attribute("Version")).Should().Be("0.0.0.0", "Nerdbank.GitVersioning stamps the real version at build time");
    }

    private static readonly XNamespace _com = "http://schemas.microsoft.com/appx/manifest/com/windows10";
    private static readonly XNamespace _desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";

    [TestMethod]
    public void ToastActivation_ComServerMatchesTheActivatorClsid()
    {
        var manifest = XDocument.Load(Path.Combine(FindRepoRoot(), "src", "ShowUp", "Package.appxmanifest"));

        var activator = (string?)manifest.Descendants(_desktop + "ToastNotificationActivation").Single().Attribute("ToastActivatorCLSID");
        var server = manifest.Descendants(_com + "ExeServer").Single();

        activator.Should().NotBeNullOrEmpty();
        ((string?)server.Element(_com + "Class")!.Attribute("Id")).Should().Be(activator);
        ((string?)server.Attribute("Arguments")).Should().Be("----AppNotificationActivated:", "Windows App SDK reads clicks from this argument");
        ((string?)server.Attribute("Executable")).Should().Be("ShowUp.exe");
    }

    [TestMethod]
    public void ToastActivation_ClsidIsStampedPerIdentityIntoBothPlaces()
    {
        // The manifest GUID is a placeholder; the build writes a UUIDv5 of ApplicationId into the activator and the COM class.
        var targets = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "WorktreeIdentity.targets"));

        targets.Should().Contain("Target Name=\"StampToastActivatorClsid\"");
        targets.Should().Contain("<NameBasedGuid Name=\"$(ApplicationId)\">");
        targets.Should().Contain("//desktop:ToastNotificationActivation/@ToastActivatorCLSID");
        targets.Should().Contain("//com:ExeServer[@Arguments='----AppNotificationActivated:']/com:Class/@Id");
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
