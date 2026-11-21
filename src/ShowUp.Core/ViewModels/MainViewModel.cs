using ShowUp.Core.Tracking;

namespace ShowUp.Core.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IStringLocalizer _localizer;
    private readonly ITrackingRepository _repository;

    public MainViewModel(IStringLocalizer localizer, ITrackingRepository repository)
    {
        _localizer = localizer;
        _repository = repository;
        PageTitle = _localizer["ApplicationName"];
    }

    // Storage spike (docs/storage.md): DEBUG-only diagnostic, not user-facing.
    [ObservableProperty]
    public partial int BonusRowCount { get; set; }

    public override async void ViewLoaded() => await RefreshBonusRowsAsync();

    [RelayCommand]
    private async Task AddBonusRowAsync()
    {
        await _repository.LogBonusAsync(null, "debug", DateOnly.FromDateTime(DateTime.Today), null);
        await RefreshBonusRowsAsync();
    }

    private async Task RefreshBonusRowsAsync()
    {
        var rows = await _repository.GetBonusWorkoutsAsync(DateOnly.MinValue, DateOnly.MaxValue);
        BonusRowCount = rows.Count;
#if DEBUG
        Console.WriteLine($"ShowUp storage: bonus rows = {rows.Count}");
#endif
    }
}
