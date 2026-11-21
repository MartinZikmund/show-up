using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;

namespace ShowUp.Core.Tests.Packaging;

[TestClass]
public class AndroidManifestTests
{
    private static readonly XNamespace _android = "http://schemas.android.com/apk/res/android";

    [TestMethod]
    public void Manifest_DeclaresInternetPermission()
    {
        // Debug builds add INTERNET on their own, so only a Release build would reveal it missing.
        var manifest = XDocument.Load(Path.Combine(FindRepoRoot(), "src", "ShowUp", "Platforms", "Android", "AndroidManifest.xml"));

        var permissions = manifest.Root!.Elements("uses-permission").Select(e => (string?)e.Attribute(_android + "name"));

        permissions.Should().Contain("android.permission.INTERNET");
    }

    [TestMethod]
    public void Manifest_DeclaresVibratePermission()
    {
        // VibrationDevice throws a SecurityException on Android without it.
        var manifest = XDocument.Load(Path.Combine(FindRepoRoot(), "src", "ShowUp", "Platforms", "Android", "AndroidManifest.xml"));

        var permissions = manifest.Root!.Elements("uses-permission").Select(e => (string?)e.Attribute(_android + "name"));

        permissions.Should().Contain("android.permission.VIBRATE");
    }

    [TestMethod]
    [DataRow("android.permission.POST_NOTIFICATIONS")]
    [DataRow("android.permission.SCHEDULE_EXACT_ALARM")]
    [DataRow("android.permission.RECEIVE_BOOT_COMPLETED")]
    public void Manifest_DeclaresReminderPermissions(string permission)
    {
        var permissions = LoadManifest().Root!.Elements("uses-permission").Select(e => (string?)e.Attribute(_android + "name"));

        permissions.Should().Contain(permission);
    }

    [TestMethod]
    public void Manifest_DoesNotUseExactAlarmPermissionReservedForAlarmApps()
    {
        // Play only allows USE_EXACT_ALARM for alarm clocks and calendars; SCHEDULE_EXACT_ALARM falls back to inexact alarms.
        var permissions = LoadManifest().Root!.Elements("uses-permission").Select(e => (string?)e.Attribute(_android + "name"));

        permissions.Should().NotContain("android.permission.USE_EXACT_ALARM");
    }

    [TestMethod]
    [DataRow("android.intent.action.BOOT_COMPLETED")]
    [DataRow("android.intent.action.TIME_SET")]
    [DataRow("android.intent.action.TIMEZONE_CHANGED")]
    [DataRow("android.intent.action.MY_PACKAGE_REPLACED")]
    [DataRow("android.app.action.SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED")]
    public void RescheduleReceiver_ListensFor(string action)
    {
        var receiver = Receiver("showup.reminders.ReminderRescheduleReceiver");

        receiver.Descendants("action").Select(a => (string?)a.Attribute(_android + "name")).Should().Contain(action);
    }

    [TestMethod]
    [DataRow("showup.reminders.ReminderAlarmReceiver")]
    [DataRow("showup.reminders.ReminderRescheduleReceiver")]
    public void Receivers_AreNotExported(string name) =>
        ((string?)Receiver(name).Attribute(_android + "exported")).Should().Be("false");

    [TestMethod]
    public void Receivers_MatchTheRegisteredJavaNames()
    {
        // The manifest names the receivers by hand; a renamed [Register] would silently orphan them.
        var registered = Directory.GetFiles(Path.Combine(FindRepoRoot(), "src", "ShowUp", "Platforms", "Android", "Reminders"), "*Receiver.cs")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\[Register\(""([^""]+)""\)\]").Select(m => m.Groups[1].Value))
            .ToList();
        var declared = LoadManifest().Descendants("receiver").Select(r => (string?)r.Attribute(_android + "name"));

        registered.Should().HaveCount(2);
        declared.Should().Contain(registered);
    }

    [TestMethod]
    public void NotificationIcon_Exists() =>
        File.Exists(Path.Combine(FindRepoRoot(), "src", "ShowUp", "Platforms", "Android", "Resources", "drawable", "ic_stat_reminder.xml")).Should().BeTrue();

    private static XDocument LoadManifest() =>
        XDocument.Load(Path.Combine(FindRepoRoot(), "src", "ShowUp", "Platforms", "Android", "AndroidManifest.xml"));

    private static XElement Receiver(string name) =>
        LoadManifest().Descendants("receiver").Single(r => (string?)r.Attribute(_android + "name") == name);

    [TestMethod]
    public void SoundAssets_UseAndroidSafeNames()
    {
        // Android rewrites other characters in asset names, and Uno opens the original name (unoplatform/uno#26157).
        var sounds = Directory.GetFiles(Path.Combine(FindRepoRoot(), "src", "ShowUp", "Assets", "Sounds"), "*.wav");

        sounds.Should().NotBeEmpty();
        sounds.Select(Path.GetFileName).Should().OnlyContain(name => Regex.IsMatch(name!, "^[a-z0-9_]+\\.wav$"));
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
