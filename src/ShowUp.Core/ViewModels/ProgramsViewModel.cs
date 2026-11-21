using Microsoft.Extensions.Logging;
using ShowUp.Core.Catalog;
using ShowUp.Core.CustomPrograms;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Localization;
using ShowUp.Core.Services;

namespace ShowUp.Core.ViewModels;

public sealed record FamilyOption(string? Family, string Display);

/// <summary>The catalog as a filterable grid of program cards, under the user's own schedules.</summary>
public partial class ProgramsViewModel : ViewModelBase
{
    private readonly IStringLocalizer _localizer;
    private readonly ICatalogService _catalog;
    private readonly ICustomProgramService _customPrograms;
    private readonly IEntitlementService _entitlements;
    private readonly INavigationService _navigation;
    private readonly ILogger<ProgramsViewModel> _logger;
    private IReadOnlyList<ProgramCardViewModel> _all = [];
    private SynchronizationContext? _context;
    private bool _listening;

    public ProgramsViewModel(
        IStringLocalizer localizer,
        ICatalogService catalog,
        ICustomProgramService customPrograms,
        IEntitlementService entitlements,
        INavigationService navigation,
        ILogger<ProgramsViewModel> logger)
    {
        _localizer = localizer;
        _catalog = catalog;
        _customPrograms = customPrograms;
        _entitlements = entitlements;
        _navigation = navigation;
        _logger = logger;
        CanCreateSchedule = customPrograms.CanEdit;
        PageTitle = localizer["Programs"];
        FamilyOptions = [new FamilyOption(null, localizer["ProgramsAllFamilies"])];
        SelectedFamilyOption = FamilyOptions[0];
    }

    [ObservableProperty]
    public partial IReadOnlyList<ProgramCardViewModel> Programs { get; private set; } = [];

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<string> Families { get; private set; } = [];

    /// <summary>The families plus a leading "all" entry, for the picker.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<FamilyOption> FamilyOptions { get; private set; }

    [ObservableProperty]
    public partial FamilyOption SelectedFamilyOption { get; set; }

    [ObservableProperty]
    public partial bool HasNoMatches { get; private set; }

    /// <summary>The user's own programs, oldest first; still listed (read-only) after a lapse.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomPrograms))]
    public partial IReadOnlyList<ProgramCardViewModel> CustomPrograms { get; private set; } = [];

    public bool HasCustomPrograms => CustomPrograms.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowScheduleGate))]
    [NotifyCanExecuteChangedFor(nameof(NewScheduleCommand))]
    public partial bool CanCreateSchedule { get; private set; }

    public bool ShowScheduleGate => !CanCreateSchedule;

    public string? SelectedFamily
    {
        get => SelectedFamilyOption?.Family;
        set => SelectedFamilyOption = FamilyOptions.FirstOrDefault(o => o.Family == value) ?? FamilyOptions[0];
    }

    public override void ViewLoaded()
    {
        base.ViewLoaded();
        if (!_listening)
        {
            _listening = true;
            _context = SynchronizationContext.Current;
            _entitlements.Changed += OnEntitlementsChanged;
        }

        RunInBackground();
    }

    public override void ViewUnloaded()
    {
        base.ViewUnloaded();
        if (_listening)
        {
            _listening = false;
            _entitlements.Changed -= OnEntitlementsChanged;
        }
    }

    public async Task LoadAsync()
    {
        if (_all.Count > 0)
        {
            return;
        }

        var catalog = await _catalog.GetCatalogAsync();
        _all = [.. catalog.Programs.Where(p => !p.IsCustom).Select(p => ToCard(p, 0))];
        List<ProgramCardViewModel> custom = [];
        foreach (var program in catalog.Programs.Where(p => p.IsCustom))
        {
            custom.Add(ToCard(program, await _customPrograms.GetVersionNumberAsync(program.Id, program.Revision)));
        }

        CustomPrograms = custom;

        string[] families = [.. _all.Select(p => p.Family).Distinct().Order(StringComparer.CurrentCultureIgnoreCase)];
        Families = families;
        FamilyOptions = [FamilyOptions[0], .. families.Select(f => new FamilyOption(f, f))];
        SelectedFamilyOption = FamilyOptions[0];
        ApplyFilter();
    }

    [RelayCommand]
    private void Open(ProgramCardViewModel? card)
    {
        if (card is not null)
        {
            _navigation.Navigate<ProgramDetailViewModel>(card.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateSchedule))]
    private void NewSchedule() => _navigation.Navigate<ScheduleBuilderViewModel>(null);

    [RelayCommand]
    private void OpenPro() => _navigation.Navigate<ProViewModel>();

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedFamilyOptionChanged(FamilyOption value)
    {
        OnPropertyChanged(nameof(SelectedFamily));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string text = Filter.Trim();
        string? family = SelectedFamily;

        Programs =
        [
            .. _all.Where(p => (family is null || p.Family == family)
                && (text.Length == 0 || p.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))),
        ];
        HasNoMatches = _all.Count > 0 && Programs.Count == 0;
    }

    private ProgramCardViewModel ToCard(WorkoutProgram program, int versionNumber)
    {
        bool official = program.Verification == VerificationLevel.Official;
        int totalDays = program.Tracks.Count == 0 ? 0 : program.Tracks.Max(t => _catalog.GetTrackDays(t).Count);
        string second = program.IsCustom
            ? _localizer.Format("ProgramWeeks", program.Tracks.FirstOrDefault()?.Phases.Sum(p => p.Weeks.Count) ?? 0)
            : _localizer.Format("ProgramTracks", program.Tracks.Count);

        return new ProgramCardViewModel(
            program.Id,
            program.Name,
            program.IsCustom ? _localizer["CustomProgramFamily"] : program.Family,
            totalDays,
            program.Tracks.Count,
            program.AccentColor,
            program.HeroImage,
            program.Verification,
            _localizer["ProgramCardSummaryFormat", _localizer.Format("ProgramDays", totalDays), second].Value,
            program.IsCustom ? _localizer["CustomProgramBadgeFormat", versionNumber] : _localizer[official ? "VerificationOfficial" : "VerificationCommunity"],
            program.IsCustom ? _localizer["CustomProgramBadgeTooltip"] : _localizer[official ? "VerificationOfficialTooltip" : "VerificationCommunityTooltip"]);
    }

    private void OnEntitlementsChanged(object? sender, EventArgs e)
    {
        if (_context is null || _context == SynchronizationContext.Current)
        {
            CanCreateSchedule = _customPrograms.CanEdit;
        }
        else
        {
            _context.Post(_ => CanCreateSchedule = _customPrograms.CanEdit, null);
        }
    }

    private async void RunInBackground()
    {
        try
        {
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading the program catalog failed.");
        }
    }
}
