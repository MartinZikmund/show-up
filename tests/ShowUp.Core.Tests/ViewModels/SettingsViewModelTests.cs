using System.Diagnostics.CodeAnalysis;
using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml;
using MZikmund.Toolkit.WinUI.Services;
using ShowUp.Core.Infrastructure;
using ShowUp.Core.Marking;
using ShowUp.Core.Messages;
using ShowUp.Core.Tracking;
using ShowUp.Core.ViewModels;
using ShowUp.Services.Settings;
using ShowUp.Services.Theming;

namespace ShowUp.Core.Tests.ViewModels;

[TestClass]
public sealed class SettingsViewModelTests
{
    [TestMethod]
    public void OnNavigatedTo_StoredMarkingSettings_LoadsThemWithoutWriting()
    {
        FakeAppPreferences preferences = new()
        {
            MarkerSoundEnabled = false,
            MarkerHapticsEnabled = false,
            CrossMomentMode = CrossMomentMode.Always,
            CrossOffStyle = CrossOffStyle.AutoMarker,
        };
        preferences.Writes.Clear();
        var viewModel = CreateViewModel(preferences);

        viewModel.OnNavigatedTo(null);

        viewModel.MarkerSoundEnabled.Should().BeFalse();
        viewModel.MarkerHapticsEnabled.Should().BeFalse();
        viewModel.CrossMomentMode.Should().Be(CrossMomentMode.Always);
        viewModel.CrossOffStyle.Should().Be(CrossOffStyle.AutoMarker);
        preferences.Writes.Should().BeEmpty();
    }

    [TestMethod]
    public void MarkerSoundEnabled_Changed_Persists()
    {
        FakeAppPreferences preferences = new();
        var viewModel = Navigated(preferences);

        viewModel.MarkerSoundEnabled = false;

        preferences.MarkerSoundEnabled.Should().BeFalse();
        preferences.Writes.Should().Equal(nameof(IAppPreferences.MarkerSoundEnabled));
    }

    [TestMethod]
    public void MarkerHapticsEnabled_Changed_Persists()
    {
        FakeAppPreferences preferences = new();
        var viewModel = Navigated(preferences);

        viewModel.MarkerHapticsEnabled = false;

        preferences.MarkerHapticsEnabled.Should().BeFalse();
        preferences.Writes.Should().Equal(nameof(IAppPreferences.MarkerHapticsEnabled));
    }

    [TestMethod]
    public void CrossMomentMode_Changed_Persists()
    {
        FakeAppPreferences preferences = new();
        var viewModel = Navigated(preferences);

        viewModel.CrossMomentMode = CrossMomentMode.Never;

        preferences.CrossMomentMode.Should().Be(CrossMomentMode.Never);
        preferences.Writes.Should().Equal(nameof(IAppPreferences.CrossMomentMode));
    }

    [TestMethod]
    public void CrossOffStyle_Changed_Persists()
    {
        FakeAppPreferences preferences = new();
        var viewModel = Navigated(preferences);

        viewModel.CrossOffStyle = CrossOffStyle.AutoMarker;

        preferences.CrossOffStyle.Should().Be(CrossOffStyle.AutoMarker);
        preferences.Writes.Should().Equal(nameof(IAppPreferences.CrossOffStyle));
    }

    [TestMethod]
    public void CrossOffStyleOptions_StartWithDrawYourself()
    {
        var viewModel = CreateViewModel(new FakeAppPreferences());

        viewModel.CrossOffStyleOptions.Should().Equal(CrossOffStyle.DrawYourself, CrossOffStyle.AutoMarker);
    }

    [TestMethod]
    public void CrossMomentModeOptions_StartWithTheDefault()
    {
        var viewModel = CreateViewModel(new FakeAppPreferences());

        viewModel.CrossMomentModeOptions.Should().Equal(CrossMomentMode.OnlyToday, CrossMomentMode.Always, CrossMomentMode.Never);
    }

    [TestMethod]
    public void SettingsViewModel_MarkerColor_PersistsOnChange()
    {
        FakeAppPreferences preferences = new();
        var viewModel = Navigated(preferences);

        viewModel.MarkerColor = MarkerColor.Blue;

        preferences.MarkerColor.Should().Be(MarkerColor.Blue);
        preferences.Writes.Should().Equal(nameof(IAppPreferences.MarkerColor));
    }

    [TestMethod]
    public void MarkerColor_Changed_TellsTheApp()
    {
        FakeAppPreferences preferences = new();
        WeakReferenceMessenger messenger = new();
        var viewModel = CreateViewModel(preferences, messenger);
        viewModel.OnNavigatedTo(null);
        List<MarkerColor> received = [];
        messenger.Register<MarkerColorChangedMessage>(this, (_, m) => received.Add(m.Color));

        viewModel.MarkerColor = MarkerColor.Green;

        received.Should().Equal(MarkerColor.Green);
    }

    [TestMethod]
    public void OnNavigatedTo_StoredMarkerColor_LoadsItWithoutWriting()
    {
        FakeAppPreferences preferences = new() { MarkerColor = MarkerColor.Purple };
        preferences.Writes.Clear();
        var viewModel = CreateViewModel(preferences);

        viewModel.OnNavigatedTo(null);

        viewModel.MarkerColor.Should().Be(MarkerColor.Purple);
        viewModel.SelectedMarkerSwatch!.Color.Should().Be(MarkerColor.Purple);
        viewModel.MarkerSwatches.Where(s => s.IsSelected).Select(s => s.Color).Should().Equal(MarkerColor.Purple);
        preferences.Writes.Should().BeEmpty();
    }

    [TestMethod]
    public void MarkerSwatches_AreAutoPlusSixInOrder()
    {
        var viewModel = CreateViewModel(new FakeAppPreferences());

        viewModel.MarkerSwatches.Select(s => s.AutomationId).Should().Equal(
            "MarkerColor_Auto", "MarkerColor_Red", "MarkerColor_Blue", "MarkerColor_Green", "MarkerColor_Purple", "MarkerColor_Orange", "MarkerColor_Contrast");
    }

    [TestMethod]
    public void SelectedMarkerSwatch_Set_ChangesTheColor()
    {
        FakeAppPreferences preferences = new();
        var viewModel = Navigated(preferences);

        viewModel.SelectedMarkerSwatch = viewModel.MarkerSwatches.Single(s => s.Color == MarkerColor.Orange);

        preferences.MarkerColor.Should().Be(MarkerColor.Orange);
    }

    [TestMethod]
    public void OnNavigatedTo_DefaultMarkerColor_SelectsAuto()
    {
        var viewModel = Navigated(new FakeAppPreferences());

        viewModel.MarkerSwatches.Where(s => s.IsSelected).Select(s => s.Color).Should().Equal(MarkerColor.Auto);
    }

    [TestMethod]
    public void MarkerColor_Changed_RaisesSelectedMarkerSwatch()
    {
        var viewModel = Navigated(new FakeAppPreferences());
        List<string?> raised = [];
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        viewModel.MarkerColor = MarkerColor.Orange;

        raised.Should().Contain(nameof(SettingsViewModel.SelectedMarkerSwatch));
    }

    [TestMethod]
    public void CanChangeMarkerColor_ForNow_IsTrue()
    {
        CreateViewModel(new FakeAppPreferences()).CanChangeMarkerColor.Should().BeTrue();
    }

    private static SettingsViewModel Navigated(FakeAppPreferences preferences)
    {
        var viewModel = CreateViewModel(preferences);
        viewModel.OnNavigatedTo(null);
        preferences.Writes.Clear();
        return viewModel;
    }

    private static SettingsViewModel CreateViewModel(FakeAppPreferences preferences, IMessenger? messenger = null) =>
        new(new FakeStringLocalizer(), preferences, new FakeThemeManager(), new FakePreferences(), new FakeApplication(), messenger ?? new WeakReferenceMessenger());

    private sealed class FakeAppPreferences : IAppPreferences
    {
        public List<string> Writes { get; } = [];

        public int DataVersion { get; set; }

        public bool FirstStart { get; set; }

        public int LaunchCount { get; set; }

        public bool OfferUserRating { get; set; }

        public ElementTheme Theme
        {
            get;
            set => Record(nameof(Theme), () => field = value);
        }

        public bool MarkerSoundEnabled
        {
            get;
            set => Record(nameof(MarkerSoundEnabled), () => field = value);
        } = true;

        public bool MarkerHapticsEnabled
        {
            get;
            set => Record(nameof(MarkerHapticsEnabled), () => field = value);
        } = true;

        public CrossMomentMode CrossMomentMode
        {
            get;
            set => Record(nameof(CrossMomentMode), () => field = value);
        } = CrossMomentMode.OnlyToday;

        public CrossOffStyle CrossOffStyle
        {
            get;
            set => Record(nameof(CrossOffStyle), () => field = value);
        } = CrossOffStyle.DrawYourself;

        public MarkerColor MarkerColor
        {
            get;
            set => Record(nameof(MarkerColor), () => field = value);
        }

        public UnitSystem UnitSystem
        {
            get;
            set => Record(nameof(UnitSystem), () => field = value);
        }

        private void Record(string name, Action write)
        {
            Writes.Add(name);
            write();
        }
    }

    private sealed class FakeStringLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class FakeThemeManager : IThemeManager
    {
        public ElementTheme CurrentTheme { get; private set; }

        public ApplicationTheme ActualTheme => ApplicationTheme.Light;

        public void SetTheme(ElementTheme theme) => CurrentTheme = theme;

        public void Dispose()
        {
        }
    }

    private sealed class FakeApplication : IApplication
    {
        public ApplicationTheme RequestedTheme => ApplicationTheme.Light;

        public ResourceDictionary Resources => throw new NotSupportedException();

        public string AppVersion => "1.0.0";

        public string? WorktreeName => null;

        public void Exit()
        {
        }
    }

    private sealed class FakePreferences : IPreferences
    {
        public T Get<T>(string key, T defaultValue) => defaultValue;

        public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value)
        {
            value = default;
            return false;
        }

        public void Set<T>(string key, T value)
        {
        }

        public T GetComplex<T>(string key, T defaultValue) => defaultValue;

        public bool TryGetComplex<T>(string key, [MaybeNullWhen(false)] out T value)
        {
            value = default;
            return false;
        }

        public void SetComplex<T>(string key, T value)
        {
        }

        public bool ContainsKey(string key) => false;

        public void Remove(string key)
        {
        }

        public void Clear()
        {
        }
    }
}
