using Microsoft.Extensions.Logging;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IStringLocalizer _localizer;
#if DEBUG
    private readonly ITrackingRepository _repository;
    private readonly ILogger<MainViewModel> _logger;
#endif

    public MainViewModel(IStringLocalizer localizer, ITrackingRepository repository, ILogger<MainViewModel> logger)
    {
        _localizer = localizer;
#if DEBUG
        _repository = repository;
        _logger = logger;
#endif
        PageTitle = _localizer["ApplicationName"];
    }

#if DEBUG
    // Storage spike (docs/storage.md): DEBUG-only diagnostic, not user-facing.
    [ObservableProperty]
    public partial int BonusRowCount { get; set; }

    public override async void ViewLoaded()
    {
        try
        {
            await RefreshBonusRowsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load bonus rows");
        }
    }

    [RelayCommand]
    private async Task AddBonusRowAsync()
    {
        try
        {
            await _repository.LogBonusAsync(null, "debug", DateOnly.FromDateTime(DateTime.Today), null);
            await RefreshBonusRowsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add bonus row");
        }
    }

    private async Task RefreshBonusRowsAsync()
    {
        var rows = await _repository.GetBonusWorkoutsAsync(DateOnly.MinValue, DateOnly.MaxValue);
        BonusRowCount = rows.Count;
        Console.WriteLine($"ShowUp storage: bonus rows = {rows.Count}");
    }
#endif
}
