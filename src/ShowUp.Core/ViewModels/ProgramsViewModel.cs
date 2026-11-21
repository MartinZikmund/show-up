using Microsoft.Extensions.Logging;
using ShowUp.Core.Catalog;
using ShowUp.Core.Localization;
using ShowUp.Core.Services;

namespace ShowUp.Core.ViewModels;

public sealed record FamilyOption(string? Family, string Display);

/// <summary>The catalog as a filterable grid of program cards.</summary>
public partial class ProgramsViewModel : ViewModelBase
{
    private readonly IStringLocalizer _localizer;
    private readonly ICatalogService _catalog;
    private readonly INavigationService _navigation;
    private readonly ILogger<ProgramsViewModel> _logger;
    private IReadOnlyList<ProgramCardViewModel> _all = [];

    public ProgramsViewModel(IStringLocalizer localizer, ICatalogService catalog, INavigationService navigation, ILogger<ProgramsViewModel> logger)
    {
        _localizer = localizer;
        _catalog = catalog;
        _navigation = navigation;
        _logger = logger;
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

    public string? SelectedFamily
    {
        get => SelectedFamilyOption?.Family;
        set => SelectedFamilyOption = FamilyOptions.FirstOrDefault(o => o.Family == value) ?? FamilyOptions[0];
    }

    public override void ViewLoaded()
    {
        base.ViewLoaded();
        RunInBackground();
    }

    public async Task LoadAsync()
    {
        if (_all.Count > 0)
        {
            return;
        }

        var catalog = await _catalog.GetCatalogAsync();
        _all = [.. catalog.Programs.Select(ToCard)];

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

    private ProgramCardViewModel ToCard(WorkoutProgram program)
    {
        bool official = program.Verification == VerificationLevel.Official;
        int totalDays = program.Tracks.Count == 0 ? 0 : program.Tracks.Max(t => _catalog.GetTrackDays(t).Count);

        return new ProgramCardViewModel(
            program.Id,
            program.Name,
            program.Family,
            totalDays,
            program.Tracks.Count,
            program.AccentColor,
            program.HeroImage,
            program.Verification,
            _localizer["ProgramCardSummaryFormat", _localizer.Format("ProgramDays", totalDays), _localizer.Format("ProgramTracks", program.Tracks.Count)].Value,
            _localizer[official ? "VerificationOfficial" : "VerificationCommunity"],
            _localizer[official ? "VerificationOfficialTooltip" : "VerificationCommunityTooltip"]);
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
