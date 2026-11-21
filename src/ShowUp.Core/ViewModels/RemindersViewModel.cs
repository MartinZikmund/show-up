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
public sealed partial class WeekdayToggle(int bit, string label, string fullName, Action changed) : ObservableObject
{
    public int Bit { get; } = bit;

    public string Label { get; } = label;

    /// <summary>"Monday": the short label alone is not enough for a screen reader.</summary>
    public string FullName { get; } = fullName;

    public string AutomationId => $"ReminderDay{Bit}";

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    partial void OnIsCheckedChanged(bool value) => changed();
}

/// <summary>A reminder row: time, the weekdays it fires on and an on/off switch.</summary>
public sealed partial class ReminderItemViewModel : ObservableObject
{
    private bool _syncing;

    internal ReminderItemViewModel(
        ReminderEntity reminder,
        string timeText,
        IReadOnlyList<string> days,
        Func<ReminderItemViewModel, Task> toggled,
        Func<ReminderItemViewModel, Task> edit,
        Func<ReminderItemViewModel, Task> delete,
        Action openPro)
    {
        Id = reminder.Id;
        MinutesOfDay = reminder.MinutesOfDay;
        DaysMask = reminder.DaysMask;
        TimeText = timeText;
        Days = days;
        _syncing = true;
        IsEnabled = reminder.Enabled;
        _syncing = false;
        ToggleCommand = new AsyncRelayCommand(() => toggled(this), AsyncRelayCommandOptions.AllowConcurrentExecutions);
        OpenProCommand = new RelayCommand(openPro);
        EditCommand = new AsyncRelayCommand(() => edit(this));
        DeleteCommand = new AsyncRelayCommand(() => delete(this));
    }

    public Guid Id { get; }

    public int MinutesOfDay { get; }

    public int DaysMask { get; }

    public string TimeText { get; }

    public IReadOnlyList<string> Days { get; }

    public string AutomationId => $"Reminder_{Id:N}";

    public IAsyncRelayCommand ToggleCommand { get; }

    public IRelayCommand OpenProCommand { get; }

    public IAsyncRelayCommand EditCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    /// <summary>False for a reminder that is off and would go over the limit; the switch is disabled instead of bouncing back.</summary>
    [ObservableProperty]
    public partial bool CanEnable { get; set; } = true;

    /// <summary>The disabled switch is because of Free, so offer Pro.</summary>
    [ObservableProperty]
    public partial bool ShowGate { get; set; }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_syncing)
        {
            ToggleCommand.Execute(null);
        }
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
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IStringLocalizer _localizer;
    private readonly IMessenger _messenger;
    private readonly ILogger<RemindersViewModel> _logger;
    private SynchronizationContext? _context;
    private Guid? _editingId;
    private bool _listening;
    private int _sending;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private Task? _permission;

    public RemindersViewModel(
        IReminderRepository repository,
        IReminderScheduler scheduler,
        IEntitlementService entitlements,
        IConfirmationDialogService confirmation,
        IDialogService dialogs,
        INavigationService navigation,
        IStringLocalizer localizer,
        IMessenger messenger,
        ILogger<RemindersViewModel> logger)
    {
        _repository = repository;
        _scheduler = scheduler;
        _entitlements = entitlements;
        _confirmation = confirmation;
        _dialogs = dialogs;
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

    public bool IsUnsupported => !_scheduler.IsSupported;

    /// <summary>Where delivery stops short of the OS: "only while the app is running" or "only while a tab is open".</summary>
    public string? LimitNote => !_scheduler.IsSupported ? null : _scheduler.Limit switch
    {
        ReminderDeliveryLimit.WhileAppRunning => _localizer["ReminderOnlyWhileRunning"].Value,
        ReminderDeliveryLimit.WhileTabOpen => _localizer["ReminderOnlyWhileTabOpen"].Value,
        _ => null,
    };

    public bool HasLimitNote => LimitNote is not null;

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
            _messenger.Register<RemindersViewModel, RemindersChangedMessage>(this, static (r, m) => r.OnRemindersChanged());
            _messenger.Register<RemindersViewModel, DataChangedMessage>(this, static (r, m) => r.OnDataChanged());
        }

        if (IsEditing)
        {
            return;
        }

        BuildWeekdays();
        RunInBackground(LoadAsync);
    }

    public override void OnNavigatedFrom()
    {
        base.OnNavigatedFrom();
        if (_listening)
        {
            _listening = false;
            _entitlements.Changed -= OnEntitlementsChanged;
            _messenger.Unregister<RemindersChangedMessage>(this);
            _messenger.Unregister<DataChangedMessage>(this);
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
                Items.Add(new ReminderItemViewModel(reminder, TimeText(reminder.MinutesOfDay), DayLabels(reminder.DaysMask), ToggleAsync, EditAsync, DeleteAsync, OpenPro));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading the reminders failed.");
        }

        RaiseLimitChanged();
        await RefreshPermissionAsync();
    }

    // Notifications can be switched off in system settings at any time; show it while a reminder is on.
    private async Task RefreshPermissionAsync()
    {
        if (!_scheduler.IsSupported || !Items.Any(i => i.IsEnabled) || _permission is { IsCompleted: false })
        {
            PermissionDenied = false;
            return;
        }

        try
        {
            PermissionDenied = await _scheduler.IsDeniedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Checking notification permission failed.");
        }
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
        var editingId = _editingId;

        // Asked before the write: browsers only show the prompt inside the click's user gesture.
        var permission = editingId is null && CanAdd ? AskPermissionAsync() : Task.CompletedTask;
        await GuardAsync(async () =>
        {
            if (editingId is { } id)
            {
                await _repository.UpdateAsync(id, minutes, mask);
            }
            else if (CanAdd)
            {
                await _repository.AddAsync(minutes, mask);
            }

            // Only now: a failed write keeps what the user picked on screen.
            IsEditing = false;
            await permission;

            await LoadAsync();
            SendChanged();
        });
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

        await GuardAsync(async () =>
        {
            await _repository.DeleteAsync(item.Id);
            await LoadAsync();
            SendChanged();
        });
    }

    // Writes are serialized and store the switch as it is now, so a flip during a pending permission prompt isn't lost.
    private Task ToggleAsync(ReminderItemViewModel item)
    {
        // Before the write, inside the switch's user gesture (see SaveAsync).
        var permission = item.IsEnabled ? AskPermissionAsync() : Task.CompletedTask;
        return GuardAsync(() => WriteToggleAsync(item, permission));
    }

    private async Task WriteToggleAsync(ReminderItemViewModel item, Task permission)
    {
        bool enabled;
        await _writeGate.WaitAsync();
        try
        {
            enabled = item.IsEnabled;
            await _repository.SetEnabledAsync(item.Id, enabled);
            RefreshLimits();
        }
        finally
        {
            _writeGate.Release();
        }

        if (enabled)
        {
            await permission;
        }

        SendChanged();
    }

    /// <summary>Runs a write; a failure is logged, shown, and the list reloaded so it matches what is stored.</summary>
    private async Task GuardAsync(Func<Task> write)
    {
        try
        {
            await write();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Changing the reminders failed.");
            try
            {
                await _dialogs.ShowAsync(_localizer["ReminderErrorTitle"], _localizer["ReminderErrorText"]);
            }
            catch (Exception dialogEx)
            {
                _logger.LogWarning(dialogEx, "Showing the reminder error failed.");
            }

            await LoadAsync();
        }
    }

    // The coordinator and this page both send it; skip our own echo, we already reloaded.
    private void SendChanged()
    {
        Interlocked.Increment(ref _sending);
        try
        {
            _messenger.Send(new RemindersChangedMessage());
        }
        finally
        {
            Interlocked.Decrement(ref _sending);
        }
    }

    // Another account's (or a synced) list: an edit in progress may name a reminder that isn't there any more.
    private void OnDataChanged() => Post(() =>
    {
        IsEditing = false;
        RunInBackground(LoadAsync);
    });

    private void OnRemindersChanged()
    {
        if (Volatile.Read(ref _sending) == 0)
        {
            Post(() => RunInBackground(LoadAsync));
        }
    }

    private Task AskPermissionAsync()
    {
        if (!_scheduler.IsSupported)
        {
            return Task.CompletedTask;
        }

        // One prompt at a time: a second toggle waits for the answer already being asked for.
        if (_permission is { IsCompleted: false } pending)
        {
            return pending;
        }

        return _permission = AskPermissionCoreAsync();
    }

    private async Task AskPermissionCoreAsync()
    {
        try
        {
            PermissionDenied = !await _scheduler.RequestPermissionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Asking for notification permission failed.");
        }
        finally
        {
            _permission = null;
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
            .. WeekOrder().Select(dow => new WeekdayToggle(Bit(dow), Culture.DateTimeFormat.AbbreviatedDayNames[(int)dow], Culture.DateTimeFormat.DayNames[(int)dow], OnWeekdayChanged))
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

    private void RefreshLimits()
    {
        var max = FeatureGate.MaxReminders(_entitlements.IsPro);
        var full = Items.Count(i => i.IsEnabled) >= max;
        foreach (var item in Items)
        {
            item.CanEnable = item.IsEnabled || !full;
            item.ShowGate = !item.CanEnable && !_entitlements.IsPro;
        }
    }

    private void RaiseLimitChanged()
    {
        RefreshLimits();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(ShowGate));
        StartAddCommand.NotifyCanExecuteChanged();
    }

    private void OnEntitlementsChanged(object? sender, EventArgs e) => Post(() => RunInBackground(LoadAsync));

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

    // Fire-and-forget loads must never take the app down.
    private async void RunInBackground(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading the reminders failed.");
        }
    }
}
