using System.Windows.Input;
using ShowUp.Core.Navigation;

namespace ShowUp.Controls;

/// <summary>Phone-width section switcher: selection flows in via <see cref="SelectedSection"/>, taps go out via <see cref="NavigateCommand"/>.</summary>
public sealed partial class BottomTabBar : UserControl
{
    public static readonly DependencyProperty SelectedSectionProperty = DependencyProperty.Register(
        nameof(SelectedSection),
        typeof(NavigationSection),
        typeof(BottomTabBar),
        new PropertyMetadata(NavigationSection.Today, (d, _) => ((BottomTabBar)d).UpdateCheckedTab()));

    public static readonly DependencyProperty NavigateCommandProperty = DependencyProperty.Register(
        nameof(NavigateCommand),
        typeof(ICommand),
        typeof(BottomTabBar),
        new PropertyMetadata(null));

    private readonly Dictionary<NavigationSection, RadioButton> _tabs;

    public BottomTabBar()
    {
        InitializeComponent();
        _tabs = new()
        {
            [NavigationSection.Today] = TodayTab,
            [NavigationSection.Calendar] = CalendarTab,
            [NavigationSection.Programs] = ProgramsTab,
            [NavigationSection.History] = HistoryTab,
            [NavigationSection.Settings] = SettingsTab,
        };
        UpdateCheckedTab();
    }

    public NavigationSection SelectedSection
    {
        get => (NavigationSection)GetValue(SelectedSectionProperty);
        set => SetValue(SelectedSectionProperty, value);
    }

    public ICommand? NavigateCommand
    {
        get => (ICommand?)GetValue(NavigateCommandProperty);
        set => SetValue(NavigateCommandProperty, value);
    }

    private void UpdateCheckedTab()
    {
        // The DP default can fire before the constructor has filled the map.
        if (_tabs is not null)
        {
            _tabs[SelectedSection].IsChecked = true;
        }
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        var section = _tabs.First(pair => pair.Value == sender).Key;
        if (section != SelectedSection && NavigateCommand?.CanExecute(section) == true)
        {
            NavigateCommand.Execute(section);
        }
    }
}
