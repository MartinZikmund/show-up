namespace ShowUp.Core.ViewModels;

public partial class ProgramsViewModel : ViewModelBase
{
    public ProgramsViewModel(IStringLocalizer localizer) => PageTitle = localizer["Programs"];
}
