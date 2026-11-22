using System.Diagnostics.CodeAnalysis;
using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using MZikmund.Toolkit.WinUI.Services;
using ShowUp.Core.Marking;
using ShowUp.Core.Messages;
using ShowUp.Core.Storage;
using ShowUp.Core.Tests.Tracking;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Storage;

[TestClass]
public sealed class SyncedPersonalizationTests
{
    private readonly FakeSyncedSettings _settings = new();
    private readonly LegacyPreferences _legacy = new();
    private readonly WeakReferenceMessenger _messenger = new();

    private SyncedPersonalization Create() => new(_settings, _legacy, _messenger, NullLogger<SyncedPersonalization>.Instance);

    [TestMethod]
    public async Task Initialize_OldPerDeviceValues_MoveToSyncedSettings()
    {
        _legacy.Values["MarkerColor"] = (int)MarkerColor.Blue;
        _legacy.Values["CrossOffStyle"] = (int)CrossOffStyle.AutoMarker;
        _legacy.Values["UnitSystem"] = (int)UnitSystem.Imperial;
        var personalization = Create();

        await personalization.InitializeAsync();

        personalization.MarkerColor.Should().Be(MarkerColor.Blue);
        personalization.CrossOffStyle.Should().Be(CrossOffStyle.AutoMarker);
        personalization.UnitSystem.Should().Be(UnitSystem.Imperial);
        _settings.Values.Should().Contain("MarkerColor", ((int)MarkerColor.Blue).ToString());
        _legacy.Values.Should().BeEmpty("the old keys are gone once moved");
    }

    [TestMethod]
    public async Task Initialize_NoSyncedValue_SeedsInsteadOfSetting()
    {
        var personalization = Create();

        await personalization.InitializeAsync();

        _settings.Seeded.Should().BeEquivalentTo(["MarkerColor", "CrossOffStyle", "UnitSystem"]);
    }

    [TestMethod]
    public async Task Initialize_SyncedValueExists_WinsOverTheOldOne()
    {
        _settings.Values["MarkerColor"] = ((int)MarkerColor.Green).ToString();
        _legacy.Values["MarkerColor"] = (int)MarkerColor.Blue;
        var personalization = Create();

        await personalization.InitializeAsync();

        personalization.MarkerColor.Should().Be(MarkerColor.Green);
    }

    [TestMethod]
    public async Task Initialize_Twice_DoesNotMigrateAgain()
    {
        _legacy.Values["MarkerColor"] = (int)MarkerColor.Blue;
        var personalization = Create();
        await personalization.InitializeAsync();
        _settings.Values["MarkerColor"] = ((int)MarkerColor.Green).ToString();

        await personalization.InitializeAsync();

        personalization.MarkerColor.Should().Be(MarkerColor.Green);
    }

    [TestMethod]
    public async Task Initialize_NothingStored_UsesDefaultsAndSeedsTheUnits()
    {
        var personalization = Create();

        await personalization.InitializeAsync();

        personalization.MarkerColor.Should().Be(MarkerColor.Auto);
        personalization.CrossOffStyle.Should().Be(CrossOffStyle.DrawYourself);
        _settings.Values.Should().ContainKey("UnitSystem", "seeded once so a later region change never relabels results");
    }

    [TestMethod]
    public async Task Initialize_GarbageValue_FallsBackToTheDefault()
    {
        _settings.Values["MarkerColor"] = "nonsense";
        var personalization = Create();

        await personalization.InitializeAsync();

        personalization.MarkerColor.Should().Be(MarkerColor.Auto);
    }

    [TestMethod]
    public async Task Set_ReadsBackAtOnceAndPersistsInOrder()
    {
        var personalization = Create();

        personalization.MarkerColor = MarkerColor.Blue;
        personalization.MarkerColor = MarkerColor.Green;
        personalization.CrossOffStyle = CrossOffStyle.AutoMarker;
        await personalization.FlushAsync();

        personalization.MarkerColor.Should().Be(MarkerColor.Green);
        _settings.Values["MarkerColor"].Should().Be(((int)MarkerColor.Green).ToString());
        _settings.Values["CrossOffStyle"].Should().Be(((int)CrossOffStyle.AutoMarker).ToString());
    }

    [TestMethod]
    public async Task DataChanged_ReloadsWhatSyncBroughtIn()
    {
        var personalization = Create();
        await personalization.InitializeAsync();
        _settings.Values["MarkerColor"] = ((int)MarkerColor.Green).ToString();

        _messenger.Send(new DataChangedMessage());
        await personalization.FlushAsync();
        // The reload runs in the background; wait for it to land.
        await WaitUntilAsync(() => personalization.MarkerColor == MarkerColor.Green);

        personalization.MarkerColor.Should().Be(MarkerColor.Green);
    }

    [TestMethod]
    public async Task DataChanged_KeyWithNoValue_KeepsTheInMemoryOneWithoutWriting()
    {
        var personalization = Create();
        await personalization.InitializeAsync();
        personalization.MarkerColor = MarkerColor.Blue;
        await personalization.FlushAsync();
        // Another account signed in: its store has none of these yet.
        _settings.Values.Clear();
        var reloaded = Reloaded();

        _messenger.Send(new DataChangedMessage());
        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        personalization.MarkerColor.Should().Be(MarkerColor.Blue);
        _settings.Values.Should().BeEmpty("a fresh row would beat the account's own value on the next sync");
    }

    [TestMethod]
    public async Task DataChanged_NewMarkerColor_RepaintsTheMarker()
    {
        var personalization = Create();
        await personalization.InitializeAsync();
        _settings.Values["MarkerColor"] = ((int)MarkerColor.Green).ToString();
        List<MarkerColor> painted = [];
        _messenger.Register<MarkerColorChangedMessage>(this, (_, m) => painted.Add(m.Color));
        var reloaded = Reloaded();

        _messenger.Send(new DataChangedMessage());
        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        painted.Should().Equal(MarkerColor.Green);
    }

    [TestMethod]
    public async Task DataChanged_SameMarkerColor_DoesNotRepaint()
    {
        var personalization = Create();
        await personalization.InitializeAsync();
        var painted = 0;
        _messenger.Register<MarkerColorChangedMessage>(this, (_, _) => painted++);
        var reloaded = Reloaded();

        _messenger.Send(new DataChangedMessage());
        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        painted.Should().Be(0);
    }

    [TestMethod]
    public async Task DataChanged_AfterAnAccountSwitch_FallsBackToTheDefaultsNotThePreviousAccount()
    {
        UserScope scope = new();
        SyncedPersonalization personalization = new(_settings, _legacy, _messenger, NullLogger<SyncedPersonalization>.Instance, scope);
        await personalization.InitializeAsync();
        personalization.MarkerColor = MarkerColor.Blue;
        await personalization.FlushAsync();
        scope.Set("bob");
        _settings.Values.Clear();
        var reloaded = Reloaded();

        _messenger.Send(new DataChangedMessage());
        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        personalization.MarkerColor.Should().Be(MarkerColor.Auto, "Alice's blue is not Bob's");
        _settings.Values.Should().BeEmpty();
    }

    private TaskCompletionSource Reloaded()
    {
        TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _messenger.Register<PersonalizationReloadedMessage>(done, (r, _) => ((TaskCompletionSource)r).TrySetResult());
        return done;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(5));
        while (!condition() && await timer.WaitForNextTickAsync(timeout.Token))
        {
        }
    }

    private sealed class LegacyPreferences : IPreferences
    {
        public Dictionary<string, int> Values { get; } = [];

        public T Get<T>(string key, T defaultValue) => Values.TryGetValue(key, out var v) ? (T)(object)v : defaultValue;

        public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value)
        {
            if (Values.TryGetValue(key, out var v))
            {
                value = (T)(object)v;
                return true;
            }

            value = default;
            return false;
        }

        public void Set<T>(string key, T value) => Values[key] = (int)(object)value!;

        public T GetComplex<T>(string key, T defaultValue) => Get(key, defaultValue);

        public bool TryGetComplex<T>(string key, [MaybeNullWhen(false)] out T value) => TryGet(key, out value);

        public void SetComplex<T>(string key, T value) => Set(key, value);

        public bool ContainsKey(string key) => Values.ContainsKey(key);

        public void Remove(string key) => Values.Remove(key);

        public void Clear() => Values.Clear();
    }
}
