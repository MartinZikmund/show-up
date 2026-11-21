namespace ShowUp.Core.ViewModels;

public partial class TodayViewModel : ViewModelBase
{
    public TodayViewModel(IStringLocalizer localizer) => PageTitle = localizer["Today"];
}
