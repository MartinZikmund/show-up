namespace ShowUp.Core.ViewModels;

public partial class ProgramCalendarViewModel : ViewModelBase
{
    public ProgramCalendarViewModel(IStringLocalizer localizer) => PageTitle = localizer["Calendar"];
}
