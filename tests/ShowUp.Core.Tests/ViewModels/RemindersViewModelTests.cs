using System.Globalization;
using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using ShowUp.Core.Messages;
using ShowUp.Core.Reminders;
using ShowUp.Services.Dialogs;
using ShowUp.Core.Tests.Entitlements;
using ShowUp.Core.Tests.Reminders;
using ShowUp.Core.ViewModels;

namespace ShowUp.Core.Tests.ViewModels;

[TestClass]
public sealed class RemindersViewModelTests
{
    private readonly FakeReminderRepository _repository = new();
    private readonly FakeReminderScheduler _scheduler = new();
    private readonly FakeEntitlementService _entitlements = new() { IsPro = true };
    private readonly FakeConfirmationDialogService _confirmation = new();
    private readonly RecordingDialogService _dialogs = new();
    private readonly RecordingNavigationService _navigation = new();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly List<RemindersChangedMessage> _changes = [];

    public RemindersViewModelTests() => _messenger.Register<List<RemindersChangedMessage>, RemindersChangedMessage>(_changes, static (list, m) => list.Add(m));

    private async Task<RemindersViewModel> OpenAsync(CultureInfo? culture = null)
    {
        RemindersViewModel viewModel = new(_repository, _scheduler, _entitlements, _confirmation, _dialogs, _navigation, new FakeStringLocalizer(), _messenger, NullLogger<RemindersViewModel>.Instance)
        {
            Culture = culture ?? CultureInfo.GetCultureInfo("en-US"),
        };
        viewModel.OnNavigatedTo(null);
        await viewModel.LoadAsync();
        return viewModel;
    }

    private static void Pick(RemindersViewModel viewModel, params string[] labels)
    {
        foreach (var day in viewModel.Weekdays)
        {
            day.IsChecked = labels.Contains(day.Label);
        }
    }

    [TestMethod]
    public async Task Add_TimeAndDays_StoresItAndTellsTheCoordinator()
    {
        var viewModel = await OpenAsync();

        viewModel.StartAddCommand.Execute(null);
        viewModel.EditTime = TimeSpan.FromMinutes(7 * 60 + 15);
        Pick(viewModel, "Mon", "Wed");
        await viewModel.SaveCommand.ExecuteAsync(null);

        var stored = (await _repository.ListAsync()).Should().ContainSingle().Subject;
        stored.MinutesOfDay.Should().Be(435);
        stored.DaysMask.Should().Be(0b101, "Monday is bit 0");
        stored.Enabled.Should().BeTrue();
        viewModel.IsEditing.Should().BeFalse();
        viewModel.Items.Should().ContainSingle();
        _changes.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Add_Supported_AsksForPermission()
    {
        var viewModel = await OpenAsync();
        _scheduler.PermissionResult = false;

        viewModel.StartAddCommand.Execute(null);
        await viewModel.SaveCommand.ExecuteAsync(null);

        _scheduler.PermissionRequests.Should().Be(1);
        viewModel.PermissionDenied.Should().BeTrue();
    }

    [TestMethod]
    public async Task Add_Unsupported_StillSavesWithoutAsking()
    {
        _scheduler.IsSupported = false;
        var viewModel = await OpenAsync();

        viewModel.StartAddCommand.Execute(null);
        await viewModel.SaveCommand.ExecuteAsync(null);

        (await _repository.ListAsync()).Should().ContainSingle();
        _scheduler.PermissionRequests.Should().Be(0);
        viewModel.IsSupported.Should().BeFalse();
    }

    [TestMethod]
    public async Task Save_NoWeekdayPicked_IsNotAllowed()
    {
        var viewModel = await OpenAsync();
        viewModel.StartAddCommand.Execute(null);

        Pick(viewModel);

        viewModel.SaveCommand.CanExecute(null).Should().BeFalse();
    }

    [TestMethod]
    public async Task Weekdays_CultureWithMondayFirst_StartWithMonday()
    {
        var viewModel = await OpenAsync(CultureInfo.GetCultureInfo("cs-CZ"));

        viewModel.Weekdays.Select(d => d.Bit).Should().Equal(0, 1, 2, 3, 4, 5, 6);
        viewModel.Weekdays[0].Label.Should().Be(CultureInfo.GetCultureInfo("cs-CZ").DateTimeFormat.AbbreviatedDayNames[1]);
    }

    [TestMethod]
    public async Task Weekdays_CultureWithSundayFirst_StartWithSundayButStoreMondayAsBitZero()
    {
        var viewModel = await OpenAsync(CultureInfo.GetCultureInfo("en-US"));

        viewModel.Weekdays.Select(d => d.Bit).Should().Equal(6, 0, 1, 2, 3, 4, 5);
        viewModel.Weekdays[0].Label.Should().Be("Sun");
    }

    [TestMethod]
    public async Task Edit_ChangesTimeAndDays()
    {
        var reminder = await _repository.AddAsync(8 * 60, 127);
        var viewModel = await OpenAsync();

        viewModel.Items[0].EditCommand.Execute(null);
        viewModel.IsEditing.Should().BeTrue();
        viewModel.EditTime.Should().Be(TimeSpan.FromHours(8));
        viewModel.EditTime = TimeSpan.FromHours(9);
        Pick(viewModel, "Fri");
        await viewModel.SaveCommand.ExecuteAsync(null);

        var stored = (await _repository.ListAsync()).Single();
        stored.Id.Should().Be(reminder.Id);
        (stored.MinutesOfDay, stored.DaysMask).Should().Be((540, 0b10000));
    }

    [TestMethod]
    public async Task Toggle_Disable_StoresItAndTellsTheCoordinator()
    {
        await _repository.AddAsync(8 * 60, 127);
        var viewModel = await OpenAsync();

        viewModel.Items[0].IsEnabled = false;

        (await _repository.ListAsync()).Single().Enabled.Should().BeFalse();
        _changes.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Delete_Confirmed_RemovesIt()
    {
        await _repository.AddAsync(8 * 60, 127);
        var viewModel = await OpenAsync();

        await viewModel.Items[0].DeleteCommand.ExecuteAsync(null);

        (await _repository.ListAsync()).Should().BeEmpty();
        viewModel.Items.Should().BeEmpty();
        viewModel.IsEmpty.Should().BeTrue();
        _changes.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Delete_Declined_KeepsIt()
    {
        await _repository.AddAsync(8 * 60, 127);
        _confirmation.Result = ConfirmationResult.Denied;
        var viewModel = await OpenAsync();

        await viewModel.Items[0].DeleteCommand.ExecuteAsync(null);

        (await _repository.ListAsync()).Should().ContainSingle();
        _changes.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Free_SecondReminder_ShowsGate()
    {
        _entitlements.IsPro = false;
        await _repository.AddAsync(8 * 60, 127);

        var viewModel = await OpenAsync();

        viewModel.ShowGate.Should().BeTrue();
        viewModel.CanAdd.Should().BeFalse();
        viewModel.StartAddCommand.CanExecute(null).Should().BeFalse();
        viewModel.StartAddCommand.Execute(null);
        viewModel.IsEditing.Should().BeFalse();
    }

    [TestMethod]
    public async Task Free_FirstReminder_NoGate()
    {
        _entitlements.IsPro = false;

        var viewModel = await OpenAsync();

        viewModel.ShowGate.Should().BeFalse();
        viewModel.CanAdd.Should().BeTrue();
    }

    [TestMethod]
    public async Task Pro_ManyReminders_NoGateUntilTheLimit()
    {
        for (var i = 0; i < 19; i++)
        {
            await _repository.AddAsync(i * 10, 127);
        }

        var viewModel = await OpenAsync();
        viewModel.CanAdd.Should().BeTrue();
        await _repository.AddAsync(1000, 127);
        await viewModel.LoadAsync();

        viewModel.CanAdd.Should().BeFalse();
        viewModel.ShowGate.Should().BeFalse("Pro is at its own limit, there is nothing to unlock");
    }

    [TestMethod]
    public async Task EntitlementChanged_ReloadsTheGate()
    {
        _entitlements.IsPro = false;
        await _repository.AddAsync(8 * 60, 127);
        var viewModel = await OpenAsync();
        viewModel.ShowGate.Should().BeTrue();

        _entitlements.SetPro(true);
        await Task.Delay(50);

        viewModel.ShowGate.Should().BeFalse();
        viewModel.CanAdd.Should().BeTrue();
    }

    [TestMethod]
    public async Task Items_EveryDay_ShowsASingleChip()
    {
        await _repository.AddAsync(8 * 60, 127);
        await _repository.AddAsync(9 * 60, 0b1000001);

        var viewModel = await OpenAsync();

        viewModel.Items[0].Days.Should().Equal("ReminderEveryDay");
        viewModel.Items[1].Days.Should().Equal("Sun", "Mon");
    }

    [TestMethod]
    public async Task OpenPro_NavigatesToTheOffer()
    {
        var viewModel = await OpenAsync();

        viewModel.OpenProCommand.Execute(null);

        _navigation.Navigations.Should().ContainSingle().Which.ViewModel.Should().Be(typeof(ProViewModel));
    }

    [TestMethod]
    public async Task Weekdays_HaveFullNamesForScreenReaders()
    {
        var english = await OpenAsync(CultureInfo.GetCultureInfo("en-US"));
        var czech = await OpenAsync(CultureInfo.GetCultureInfo("cs-CZ"));

        english.Weekdays.Select(d => (d.Label, d.FullName)).Should().StartWith(("Sun", "Sunday"));
        english.Weekdays.Single(d => d.Bit == 0).FullName.Should().Be("Monday");
        czech.Weekdays[0].FullName.Should().Be(CultureInfo.GetCultureInfo("cs-CZ").DateTimeFormat.DayNames[1]);
    }

    [TestMethod]
    public async Task Free_OneEnabled_OtherRowsCannotBeEnabled()
    {
        _entitlements.IsPro = false;
        await _repository.AddAsync(8 * 60, 127);
        await _repository.AddAsync(9 * 60, 127, enabled: false);

        var viewModel = await OpenAsync();

        viewModel.Items[0].CanEnable.Should().BeTrue("it is the one that is on, so it can be turned off");
        viewModel.Items[1].CanEnable.Should().BeFalse();
        viewModel.Items[1].ShowGate.Should().BeTrue();
        viewModel.Items[0].ShowGate.Should().BeFalse();
    }

    [TestMethod]
    public async Task Free_NoneEnabled_AnyRowCanBeEnabled()
    {
        _entitlements.IsPro = false;
        await _repository.AddAsync(8 * 60, 127, enabled: false);

        var viewModel = await OpenAsync();

        viewModel.Items[0].CanEnable.Should().BeTrue();
    }

    [TestMethod]
    public async Task Free_SwitchingTheOnOneOff_FreesTheOthers()
    {
        _entitlements.IsPro = false;
        await _repository.AddAsync(8 * 60, 127);
        await _repository.AddAsync(9 * 60, 127, enabled: false);
        var viewModel = await OpenAsync();

        viewModel.Items[0].IsEnabled = false;
        await Task.Delay(50);

        viewModel.Items[1].CanEnable.Should().BeTrue();
        viewModel.Items[1].ShowGate.Should().BeFalse();
    }

    [TestMethod]
    public async Task Pro_AllRowsCanBeEnabled()
    {
        await _repository.AddAsync(8 * 60, 127);
        await _repository.AddAsync(9 * 60, 127, enabled: false);

        var viewModel = await OpenAsync();

        viewModel.Items.Should().OnlyContain(i => i.CanEnable && !i.ShowGate);
    }

    [TestMethod]
    public async Task Lapse_UpdatesRowsLive()
    {
        await _repository.AddAsync(8 * 60, 127);
        await _repository.AddAsync(9 * 60, 127, enabled: false);
        var viewModel = await OpenAsync();
        viewModel.Items[1].CanEnable.Should().BeTrue();

        _entitlements.SetPro(false);
        await Task.Delay(50);

        viewModel.Items[1].CanEnable.Should().BeFalse();
        viewModel.Items[1].ShowGate.Should().BeTrue();
    }

    [TestMethod]
    public async Task Toggle_RepositoryFails_ShowsErrorAndResyncs()
    {
        await _repository.AddAsync(8 * 60, 127);
        var viewModel = await OpenAsync();
        _repository.FailWrites = true;

        viewModel.Items[0].IsEnabled = false;
        await Task.Delay(100);

        _dialogs.Shown.Should().ContainSingle();
        viewModel.Items[0].IsEnabled.Should().BeTrue("the list reloads what is stored");
        _changes.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Delete_RepositoryFails_DoesNotThrowAndResyncs()
    {
        await _repository.AddAsync(8 * 60, 127);
        var viewModel = await OpenAsync();
        _repository.FailWrites = true;

        await viewModel.Items[0].DeleteCommand.ExecuteAsync(null);

        _dialogs.Shown.Should().ContainSingle();
        viewModel.Items.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Save_RepositoryFails_DoesNotThrowAndResyncs()
    {
        var viewModel = await OpenAsync();
        viewModel.StartAddCommand.Execute(null);
        _repository.FailWrites = true;

        await viewModel.SaveCommand.ExecuteAsync(null);

        _dialogs.Shown.Should().ContainSingle();
        viewModel.Items.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Save_RepositoryFails_KeepsTheEditorOpenWithWhatWasPicked()
    {
        var viewModel = await OpenAsync();
        viewModel.StartAddCommand.Execute(null);
        viewModel.EditTime = TimeSpan.FromMinutes(6 * 60 + 30);
        Pick(viewModel, "Tue");
        _repository.FailWrites = true;

        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.IsEditing.Should().BeTrue();
        viewModel.EditTime.Should().Be(TimeSpan.FromMinutes(6 * 60 + 30));
        viewModel.Weekdays.Where(d => d.IsChecked).Select(d => d.Label).Should().Equal("Tue");
    }

    [TestMethod]
    public async Task Save_Succeeds_ClosesTheEditor()
    {
        var viewModel = await OpenAsync();
        viewModel.StartAddCommand.Execute(null);

        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.IsEditing.Should().BeFalse();
    }

    [TestMethod]
    public async Task Toggle_OffWhilePermissionPromptIsOpen_StoresOff()
    {
        var reminder = await _repository.AddAsync(8 * 60, 127, enabled: false);
        var viewModel = await OpenAsync();
        _scheduler.PendingPermission = new();

        viewModel.Items[0].IsEnabled = true;
        await Task.Delay(50);
        viewModel.Items[0].IsEnabled = false;
        await Task.Delay(50);
        _scheduler.PendingPermission.SetResult(true);
        await Task.Delay(50);

        (await _repository.ListAsync()).Single(r => r.Id == reminder.Id).Enabled.Should().BeFalse();
        _repository.SetEnabledCalls.Should().Be(2);
        _scheduler.PermissionRequests.Should().Be(1, "the second toggle doesn't stack another prompt");
    }

    [TestMethod]
    public async Task Toggle_TwoRowsOnWhilePromptIsOpen_AsksOnce()
    {
        _entitlements.SetPro(true);
        await _repository.AddAsync(8 * 60, 127, enabled: false);
        await _repository.AddAsync(9 * 60, 127, enabled: false);
        var viewModel = await OpenAsync();
        _scheduler.PendingPermission = new();

        viewModel.Items[0].IsEnabled = true;
        viewModel.Items[1].IsEnabled = true;
        await Task.Delay(50);
        _scheduler.PendingPermission.SetResult(false);
        await Task.Delay(50);

        _scheduler.PermissionRequests.Should().Be(1);
        viewModel.PermissionDenied.Should().BeTrue();
        (await _repository.ListAsync()).Should().OnlyContain(r => r.Enabled);
    }

    [TestMethod]
    public async Task Save_OwnMessage_DoesNotReloadAgain()
    {
        var viewModel = await OpenAsync();
        viewModel.StartAddCommand.Execute(null);
        var before = _repository.ListCalls;

        await viewModel.SaveCommand.ExecuteAsync(null);
        await Task.Delay(50);

        (_repository.ListCalls - before).Should().Be(1, "only the save's own reload");
    }

    [TestMethod]
    public async Task RemindersChanged_FromElsewhere_Reloads()
    {
        var viewModel = await OpenAsync();
        await _repository.AddAsync(8 * 60, 127);

        _messenger.Send(new RemindersChangedMessage());
        await Task.Delay(50);

        viewModel.Items.Should().ContainSingle();
    }

    [TestMethod]
    public async Task OnNavigatedTo_EditorOpen_KeepsTheEditorAsItIs()
    {
        var viewModel = await OpenAsync();
        viewModel.StartAddCommand.Execute(null);
        var weekdays = viewModel.Weekdays;
        viewModel.Weekdays[0].IsChecked = false;
        var before = _repository.ListCalls;

        viewModel.OnNavigatedTo(null);

        viewModel.Weekdays.Should().BeSameAs(weekdays);
        viewModel.Weekdays[0].IsChecked.Should().BeFalse();
        _repository.ListCalls.Should().Be(before);
    }

    [TestMethod]
    [DataRow(ReminderDeliveryLimit.WhileAppRunning, "ReminderOnlyWhileRunning")]
    [DataRow(ReminderDeliveryLimit.WhileTabOpen, "ReminderOnlyWhileTabOpen")]
    public async Task LimitNote_LimitedDelivery_SaysWhen(ReminderDeliveryLimit limit, string key)
    {
        _scheduler.Limit = limit;

        var viewModel = await OpenAsync();

        viewModel.LimitNote.Should().Be(key);
        viewModel.HasLimitNote.Should().BeTrue();
    }

    [TestMethod]
    public async Task LimitNote_OsDelivery_IsHidden()
    {
        var viewModel = await OpenAsync();

        viewModel.HasLimitNote.Should().BeFalse();
    }

    [TestMethod]
    public async Task LimitNote_Unsupported_IsHiddenBehindTheUnsupportedNotice()
    {
        _scheduler.IsSupported = false;
        _scheduler.Limit = ReminderDeliveryLimit.WhileAppRunning;

        var viewModel = await OpenAsync();

        viewModel.HasLimitNote.Should().BeFalse();
    }

    [TestMethod]
    public async Task Add_RepositoryFails_PromptWasStillStartedBeforeTheWrite()
    {
        // The browser only shows its prompt inside the click, so it can't wait for the write.
        var viewModel = await OpenAsync();
        viewModel.StartAddCommand.Execute(null);
        _repository.FailWrites = true;

        await viewModel.SaveCommand.ExecuteAsync(null);

        _scheduler.PermissionRequests.Should().Be(1);
    }

    [TestMethod]
    public async Task Toggle_OnAndRepositoryFails_PromptWasStillStartedBeforeTheWrite()
    {
        await _repository.AddAsync(8 * 60, 127, enabled: false);
        var viewModel = await OpenAsync();
        _repository.FailWrites = true;

        viewModel.Items[0].IsEnabled = true;
        await Task.Delay(100);

        _scheduler.PermissionRequests.Should().Be(1);
    }

    [TestMethod]
    public async Task Open_ReminderOnAndNotificationsOff_ShowsTheNotice()
    {
        await _repository.AddAsync(8 * 60, 127);
        _scheduler.Denied = true;

        var viewModel = await OpenAsync();

        viewModel.PermissionDenied.Should().BeTrue();
        _scheduler.PermissionRequests.Should().Be(0, "checking never prompts");
    }

    [TestMethod]
    public async Task Open_NotificationsOffButNoReminderOn_NoNotice()
    {
        await _repository.AddAsync(8 * 60, 127, enabled: false);
        _scheduler.Denied = true;

        var viewModel = await OpenAsync();

        viewModel.PermissionDenied.Should().BeFalse();
    }

    [TestMethod]
    public async Task Load_NotificationsSwitchedBackOn_ClearsTheNotice()
    {
        await _repository.AddAsync(8 * 60, 127);
        _scheduler.Denied = true;
        var viewModel = await OpenAsync();

        _scheduler.Denied = false;
        await viewModel.LoadAsync();

        viewModel.PermissionDenied.Should().BeFalse();
    }
}
