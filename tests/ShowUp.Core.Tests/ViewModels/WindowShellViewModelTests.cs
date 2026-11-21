using AwesomeAssertions;
using Microsoft.Extensions.Localization;
using ShowUp.Core.Navigation;
using ShowUp.Core.Services;
using ShowUp.Core.ViewModels;

namespace ShowUp.Core.Tests.ViewModels;

[TestClass]
public sealed class WindowShellViewModelTests
{
    [TestMethod]
    [DataRow(NavigationSection.Today, typeof(TodayViewModel))]
    [DataRow(NavigationSection.Calendar, typeof(ProgramCalendarViewModel))]
    [DataRow(NavigationSection.Programs, typeof(ProgramsViewModel))]
    [DataRow(NavigationSection.History, typeof(HistoryViewModel))]
    [DataRow(NavigationSection.Settings, typeof(SettingsViewModel))]
    public void NavigateToSection_Section_NavigatesToMatchingViewModel(NavigationSection section, Type expectedViewModel)
    {
        FakeNavigationService navigation = new();
        WindowShellViewModel viewModel = new(navigation, new FakeStringLocalizer());

        viewModel.NavigateToSection(section);

        navigation.NavigatedViewModels.Should().Equal(expectedViewModel);
    }

    [TestMethod]
    [DataRow(NavigationSection.Today, "Today")]
    [DataRow(NavigationSection.Calendar, "Calendar")]
    [DataRow(NavigationSection.Programs, "Programs")]
    [DataRow(NavigationSection.History, "History")]
    [DataRow(NavigationSection.Settings, "Settings")]
    public void NavigateToSection_Section_SetsLocalizedTitleAndSelection(NavigationSection section, string titleKey)
    {
        WindowShellViewModel viewModel = new(new FakeNavigationService(), new FakeStringLocalizer());

        viewModel.NavigateToSection(section);

        viewModel.Title.Should().Be($"[{titleKey}]");
        viewModel.SelectedSection.Should().Be(section);
    }

    [TestMethod]
    public void NavigateToSection_AlreadyCurrent_DoesNotNavigateAgain()
    {
        FakeNavigationService navigation = new() { CurrentSection = NavigationSection.History };
        WindowShellViewModel viewModel = new(navigation, new FakeStringLocalizer());

        viewModel.NavigateToSection(NavigationSection.History);

        navigation.NavigatedViewModels.Should().BeEmpty();
    }

    [TestMethod]
    public void NavigateToSection_AnySection_ClearsBackStack()
    {
        FakeNavigationService navigation = new();
        WindowShellViewModel viewModel = new(navigation, new FakeStringLocalizer());

        viewModel.NavigateToSection(NavigationSection.Calendar);

        navigation.BackStackClearCount.Should().Be(1);
    }

    [TestMethod]
    public void SyncWithNavigation_SectionChangedOutside_UpdatesSelectionAndTitle()
    {
        FakeNavigationService navigation = new();
        WindowShellViewModel viewModel = new(navigation, new FakeStringLocalizer());
        navigation.CurrentSection = NavigationSection.Programs;

        viewModel.SyncWithNavigation();

        viewModel.SelectedSection.Should().Be(NavigationSection.Programs);
        viewModel.Title.Should().Be("[Programs]");
    }

    [TestMethod]
    public void Constructor_NoNavigation_SelectsToday()
    {
        WindowShellViewModel viewModel = new(new FakeNavigationService(), new FakeStringLocalizer());

        viewModel.SelectedSection.Should().Be(NavigationSection.Today);
    }

    private sealed class FakeNavigationService : INavigationService
    {
        public List<Type> NavigatedViewModels { get; } = [];

        public int BackStackClearCount { get; private set; }

        public bool CanGoBack => false;

        public NavigationSection? CurrentSection { get; set; }

        public void Initialize() { }

        public void Navigate<TViewModel>() => NavigatedViewModels.Add(typeof(TViewModel));

        public void Navigate<TViewModel>(object? parameter) => NavigatedViewModels.Add(typeof(TViewModel));

        public bool GoBack() => false;

        public void ClearBackStack() => BackStackClearCount++;
    }

    private sealed class FakeStringLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, $"[{name}]");

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
