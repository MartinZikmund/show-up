using ShowUp.Core.ViewModels;
using ShowUp.Services.Localization;

namespace ShowUp.Views;

public partial class ProgramResultsViewBase : ViewBase<ProgramResultsViewModel> { }

/// <summary>Reached from the calendar and from program detail, so it keeps whichever section was open.</summary>
public sealed partial class ProgramResultsView : ProgramResultsViewBase
{
    public ProgramResultsView() => InitializeComponent();

    public static string ValueOrDash(string value) => value.Length == 0 ? "–" : value;

    public static string EmptyMessage(bool hasEnrollment) => Localizer.Instance[hasEnrollment ? "ResultsEmptyMessage" : "ResultsNoEnrollment"];
}
