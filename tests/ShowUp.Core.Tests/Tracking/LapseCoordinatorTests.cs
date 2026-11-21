using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Services;
using ShowUp.Core.Tests.Entitlements;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Tests.Tracking;

[TestClass]
public sealed class LapseCoordinatorTests
{
    private static readonly DateOnly _today = new(2026, 11, 21);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeCatalogService _catalog = new(TestCatalog.Program("p", 1, "run"), TestCatalog.Program("q", 1, "abs"));
    private readonly FakeSyncedSettings _settings = new();
    private readonly FakeEntitlementService _entitlements = new() { IsPro = true };
    private readonly FakePrompt _prompt = new();
    private readonly FakeTrackingRepository _repository;
    private readonly ActiveEnrollmentService _service;
    private readonly LapseCoordinator _coordinator;

    public LapseCoordinatorTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _repository = new(_time);
        _service = TestEnrollments.Create(_repository, _catalog, new WeakReferenceMessenger(), _settings, _entitlements);
        _coordinator = new(_service, _entitlements, NullLogger<LapseCoordinator>.Instance);
        _coordinator.Attach(_prompt);
    }

    [TestMethod]
    public async Task Check_LapseWithTwo_AsksAndPersistsThePick()
    {
        var (p, q) = await EnrollTwoAsync();
        _prompt.Answer = p;
        _entitlements.IsPro = false;

        var shown = await _coordinator.CheckAsync();

        shown.Should().BeTrue();
        _prompt.Asked.Should().ContainSingle().Which.Suggested.Should().Be(q, "the most recently started is the default");
        (await _service.GetPrimaryAsync())!.Id.Should().Be(p);
    }

    [TestMethod]
    public async Task Check_Dismissed_KeepsTheMostRecentAndDoesNotAskAgain()
    {
        var (_, q) = await EnrollTwoAsync();
        _entitlements.IsPro = false;

        await _coordinator.CheckAsync();
        var again = await _coordinator.CheckAsync();

        again.Should().BeFalse();
        _prompt.Asked.Should().ContainSingle();
        (await _service.GetPrimaryAsync())!.Id.Should().Be(q);
        (await _service.HasPrimaryChoiceAsync()).Should().BeTrue();
    }

    [TestMethod]
    [DataRow(true, 2)]
    [DataRow(false, 1)]
    public async Task Check_ProOrSingleProgram_DoesNotAsk(bool isPro, int programs)
    {
        if (programs == 2)
        {
            await EnrollTwoAsync();
        }
        else
        {
            await _repository.EnrollAsync("p", 1, "t", _today);
        }

        _entitlements.IsPro = isPro;

        (await _coordinator.CheckAsync()).Should().BeFalse();
        _prompt.Asked.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Changed_ProFlipsFalse_Asks()
    {
        await EnrollTwoAsync();

        _entitlements.SetPro(false);
        await _prompt.Shown.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _prompt.Asked.Should().ContainSingle();
    }

    private async Task<(Guid P, Guid Q)> EnrollTwoAsync()
    {
        var p = (await _repository.EnrollAsync("p", 1, "t", _today)).Id;
        _time.Advance(TimeSpan.FromSeconds(1));
        var q = (await _repository.EnrollAsync("q", 1, "t", _today)).Id;
        return (p, q);
    }

    private sealed class FakePrompt : IPrimaryProgramPrompt
    {
        public Guid? Answer { get; set; }

        public List<(IReadOnlyList<ActiveEnrollment> Programs, Guid Suggested)> Asked { get; } = [];

        public TaskCompletionSource Shown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Guid?> PickAsync(IReadOnlyList<ActiveEnrollment> programs, Guid suggested)
        {
            Asked.Add((programs, suggested));
            Shown.TrySetResult();
            return Task.FromResult(Answer);
        }
    }

    [TestMethod]
    public async Task Attach_RenewedOnFirstRefresh_NeverPrompts()
    {
        await EnrollTwoAsync();
        FakeEntitlementService entitlements = new() { IsPro = false };
        entitlements.OnRefresh = () =>
        {
            entitlements.IsPro = true;
            return Task.CompletedTask;
        };
        FakePrompt prompt = new();
        LapseCoordinator coordinator = new(_service, entitlements, NullLogger<LapseCoordinator>.Instance);

        coordinator.Attach(prompt);
        var shown = await coordinator.CheckAsync();

        shown.Should().BeFalse();
        prompt.Asked.Should().BeEmpty();
        entitlements.RefreshCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task Attach_StillFreeAfterFirstRefresh_Prompts()
    {
        await EnrollTwoAsync();
        FakeEntitlementService entitlements = new() { IsPro = false };
        FakePrompt prompt = new();
        LapseCoordinator coordinator = new(_service, entitlements, NullLogger<LapseCoordinator>.Instance);

        coordinator.Attach(prompt);
        await prompt.Shown.Task.WaitAsync(TimeSpan.FromSeconds(5));

        prompt.Asked.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Changed_BeforeFirstRefreshSettles_WaitsForIt()
    {
        await EnrollTwoAsync();
        TaskCompletionSource refresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeEntitlementService entitlements = new() { IsPro = true, OnRefresh = () => refresh.Task };
        FakePrompt prompt = new();
        LapseCoordinator coordinator = new(_service, entitlements, NullLogger<LapseCoordinator>.Instance);
        coordinator.Attach(prompt);

        entitlements.SetPro(false);
        await Task.Delay(50);
        prompt.Asked.Should().BeEmpty("the launch refresh hasn't answered yet");

        entitlements.OnRefresh = null;
        refresh.SetResult();
        await prompt.Shown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        prompt.Asked.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Attach_TwoWindows_PromptsOnceInTheLatest()
    {
        await EnrollTwoAsync();
        FakeEntitlementService entitlements = new() { IsPro = false };
        FakePrompt first = new();
        FakePrompt second = new();
        LapseCoordinator coordinator = new(_service, entitlements, NullLogger<LapseCoordinator>.Instance);

        coordinator.Attach(first);
        coordinator.Attach(second);
        await Task.WhenAny(first.Shown.Task, second.Shown.Task).WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        (first.Asked.Count + second.Asked.Count).Should().Be(1);
    }
}
