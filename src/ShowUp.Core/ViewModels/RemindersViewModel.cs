using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Messages;
using ShowUp.Core.Reminders;
using ShowUp.Core.Services;
using ShowUp.Core.Storage;
using ShowUp.Services.Dialogs;

namespace ShowUp.Core.ViewModels;

/// <summary>One weekday toggle in the editor; <see cref="Bit"/> is the <see cref="ReminderEntity.DaysMask"/> bit (0 = Monday).</summary>
public sealed partial class WeekdayToggle(int bit, string label, Action changed) : ObservableObject
{
    public int Bit { get; } = bit;

    public string Label { get; } = label;

    public string AutomationId => $"ReminderDay{Bit}";

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    partial void OnIsCheckedChanged(bool value) => changed();
}

/// <summary>A reminder row: time, the weekdays it fires on and an on/off switch.</summary>
public sealed partial class ReminderItemViewModel : ObservableObject
{
    private readonly Func<ReminderItemViewModel, Task> _toggled;
    private bool _syncing;

    internal ReminderItemViewModel(
        ReminderEntity reminder,
        string timeText,
        IReadOnlyList<string> days,
        Func<ReminderItemViewModel, Task> toggled,
        Func<ReminderItemViewModel, Task> edit,
        Func<ReminderItemViewModel, Task> delete)
    {
        Id = reminder.Id;
        MinutesOfDay = reminder.MinutesOfDay;
        DaysMask = reminder.DaysMask;
        TimeText = timeText;
        Days = days;
        _toggled = toggled;
        _syncing = true;
        IsEnabled = reminder.Enabled;
        _syncing = false;
        EditCommand = new AsyncRelayCommand(() => edit(this));
        DeleteCommand = new AsyncRelayCommand(() => delete(this));
    }

    public Guid Id { get; }

    public int MinutesOfDay { get; }

    public int DaysMask { get; }

    public string TimeText { get; }

    public IReadOnlyList<string> Days { get; }

    public string AutomationId => $"Reminder_{Id:N}";

    public IAsyncRelayCommand EditCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_syncing)
        {
            _ = _toggled(this);
        }
    }

    internal void SetEnabledSilently(bool value)
    {
        _syncing = true;
        IsEnabled = value;
        _syncing = false;
    }
}

/// <summary>Workout reminders: list, add/edit, switch on and off, delete. Free keeps one; Pro up to twenty.</summary>
public partial class RemindersViewModel : ViewModelBase
{
    private const int DefaultMinutes = 18 * 60;

    private readonly IReminderRepository _repository;
    private readonly IReminderScheduler _scheduler;
    private readonly IEntitlementService _entitlements;
    private readonly IConfirmationDialogService _confirmation;
    private readonly INavigationService _navigation;
    private readonly IStringLocalizer _localizer;
    private readonly IMessenger _messenger;
    private readonly ILogger<RemindersViewModel> _logger;
    private SynchronizationContext? _context;
    private Guid? _editingId;
    private bool _listening;

    public RemindersViewModel(
        IReminderRepository repository,
        IReminderScheduler scheduler,
        IEntitlementService entitlements,
        IConfirmationDialogService confirmation,
        INavigationService navigation,
        IStringLocalizer localizer,
        IMessenger messenger,
        ILogger<RemindersViewModel> logger)
    {
        _repository = repository;
        _scheduler = scheduler;
        _entitlements = entitlements;
        _confirmation = confirmation;
        _navigation = navigation;
        _localizer = localizer;
        _messenger = messenger;
        _logger = logger;
        PageTitle = localizer["Reminders"];
    }

    /// <summary>Sets the weekday order and names; tests override it.</summary>
    public CultureInfo Culture { get; set; } = CultureInfo.CurrentCulture;

    public ObservableCollection<ReminderItemViewModel> Items { get; } = [];

    public IReadOnlyList<WeekdayToggle> Weekdays { get; private set; } = [];

    public bool IsSupported => _scheduler.IsSupported;

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Another reminder fits under the tier's limit.</summary>
    public bool CanAdd => Items.Count < FeatureGate.MaxReminders(_entitlements.IsPro);

    /// <summary>Shows "Unlock with Pro" instead of letting a Free user add a second reminder.</summary>
    public bool ShowGate => !CanAdd && !_entitlements.IsPro;

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditorTitle { get; set; } = "";

    [ObservableProperty]
    public partial TimeSpan EditTime { get; set; } = TimeSpan.FromMinutes(DefaultMinutes);

    /// <summary>Notifications were refused at the OS.</summary>
    [ObservableProperty]
    public partial bool PermissionDenied { get; set; }

    public bool CanSave => IsEditing && Weekdays.Any(d => d.IsChecked);

    public override void OnNavigatedTo(object? parameter)
    {
        base.OnNavigatedTo(parameter);
        if (!_listening)
        {
            _listening = true;
            _context = SynchronizationContext.Current;
            _entitlements.Changed += OnEntitlementsChanged;
            _messenger.Register<RemindersViewModel, RemindersChangedMessage>(this, static (r, m) => r.Post(() => _ = r.LoadAsync()));
        }

        BuildWeekdays();
        _ = LoadAsync();
    }

    public override void OnNavigatedFrom()
    {
        base.OnNavigatedFrom();
        if (_listening)
        {
            _listening = false;
            _entitlements.Changed -= OnEntitlementsChanged;
            _messenger.Unregister<RemindersChangedMessage>(this);
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            var stored = await _repository.ListAsync();
            Items.Clear();
            foreach (var reminder in stored.OrderBy(r => r.MinutesOfDay).ThenBy(r => r.CreatedAt))
            {
                Items.Add(new ReminderItemViewModel(reminder, TimeText(reminder.MinutesOfDay), DayLabels(reminder.DaysMask), ToggleAsync, EditAsync, DeleteAsync));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading the reminders failed.");
        }

        RaiseLimitChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void StartAdd()
    {
        if (!CanAdd)
        {
            return;
        }

        _editingId = null;
        EditorTitle = _localizer["ReminderAdd"];
        OpenEditor(TimeSpan.FromMinutes(DefaultMinutes), 127);
    }

    [RelayCommand]
    private void Cancel() => IsEditing = false;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var minutes = (int)EditTime.TotalMinutes;
        var mask = Weekdays.Where(d => d.IsChecked).Sum(d => 1 << d.Bit);
        try
        {
            if (_editingId is { } id)
            {
                await _repository.UpdateAsync(id, minutes, mask);
            }
            else if (CanAdd)
            {
                await _repository.AddAsync(minutes, mask);
                await AskPermissionAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving the reminder failed.");
        }

        IsEditing = false;
        await LoadAsync();
        _messenger.Send(new RemindersChangedMessage());
    }

    [RelayCommand]
    private void GoBack() => _navigation.GoBack();

    [RelayCommand]
    private void OpenPro() => _navigation.Navigate<ProViewModel>();

    private Task EditAsync(ReminderItemViewModel item)
    {
        _editingId = item.Id;
        EditorTitle = _localizer["ReminderEdit"];
        OpenEditor(TimeSpan.FromMinutes(item.MinutesOfDay), item.DaysMask);
        return Task.CompletedTask;
    }

    private async Task DeleteAsync(ReminderItemViewModel item)
    {
        var answer = await _confirmation.ShowAsync(_localizer["ReminderDeleteTitle"], _localizer["ReminderDeleteText", item.TimeText]);
        if (answer != ConfirmationResult.Confirmed)
        {
            return;
        }

        await _repository.DeleteAsync(item.Id);
        await LoadAsync();
        _messenger.Send(new RemindersChangedMessage());
    }

    private async Task ToggleAsync(ReminderItemViewModel item)
    {
        if (item.IsEnabled && Items.Count(i => i.IsEnabled) > FeatureGate.MaxReminders(_entitlements.IsPro))
        {
            // Lapsed: the extra reminders stay off until Pro is back.
            item.SetEnabledSilently(false);
            RaiseLimitChanged();
            return;
        }

        await _repository.SetEnabledAsync(item.Id, item.IsEnabled);
        if (item.IsEnabled)
        {
            await AskPermissionAsync();
        }

        _messenger.Send(new RemindersChangedMessage());
    }

    private async Task AskPermissionAsync()
    {
        if (!_scheduler.IsSupported)
        {
            return;
        }

        try
        {
            PermissionDenied = !await _scheduler.RequestPermissionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Asking for notification permission failed.");
        }
    }

    private void OpenEditor(TimeSpan time, int mask)
    {
        foreach (var day in Weekdays)
        {
            day.IsChecked = (mask & (1 << day.Bit)) != 0;
        }

        EditTime = time;
        IsEditing = true;
    }

    private void BuildWeekdays()
    {
        Weekdays =
        [
            .. WeekOrder().Select(dow => new WeekdayToggle(Bit(dow), Culture.DateTimeFormat.AbbreviatedDayNames[(int)dow], OnWeekdayChanged))
        ];
        OnPropertyChanged(nameof(Weekdays));
    }

    private void OnWeekdayChanged()
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsEditingChanged(bool value) => OnWeekdayChanged();

    private IEnumerable<DayOfWeek> WeekOrder()
    {
        var first = (int)Culture.DateTimeFormat.FirstDayOfWeek;
        return Enumerable.Range(0, 7).Select(i => (DayOfWeek)((first + i) % 7));
    }

    private static int Bit(DayOfWeek day) => ((int)day + 6) % 7;

    private IReadOnlyList<string> DayLabels(int mask)
    {
        if (mask == 127)
        {
            return [_localizer["ReminderEveryDay"].Value];
        }

        return [.. WeekOrder().Where(d => (mask & (1 << Bit(d))) != 0).Select(d => Culture.DateTimeFormat.AbbreviatedDayNames[(int)d])];
    }

    private string TimeText(int minutes) => DateTime.Today.AddMinutes(minutes).ToString("t", Culture);

    private void RaiseLimitChanged()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(ShowGate));
        StartAddCommand.NotifyCanExecuteChanged();
    }

    private void OnEntitlementsChanged(object? sender, EventArgs e) => Post(() => _ = LoadAsync());

    private void Post(Action action)
    {
        if (_context is null)
        {
            action();
        }
        else
        {
            _context.Post(_ => action(), null);
        }
    }
}
