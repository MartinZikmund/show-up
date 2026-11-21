namespace ShowUp.Core.ViewModels;

public partial class HistoryViewModel : ViewModelBase
{
    public HistoryViewModel(IStringLocalizer localizer) => PageTitle = localizer["History"];
}
