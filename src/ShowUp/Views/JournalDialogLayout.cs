namespace ShowUp.Views;

/// <summary>Sizes the notes and results dialogs: a full-height sheet below 640 px, a ~480 px dialog above.</summary>
internal static class JournalDialogLayout
{
    private const double WideWidth = 640;

    // A 480 px dialog less the dialog's own 24 px padding each side.
    private const double ContentWidth = 432;

    public static void Apply(ContentDialog dialog, FrameworkElement content) => dialog.Loaded += (_, _) => Update(dialog, content);

    private static void Update(ContentDialog dialog, FrameworkElement content)
    {
        var narrow = (dialog.XamlRoot?.Size.Width ?? WideWidth) < WideWidth;
        dialog.FullSizeDesired = narrow;
        content.Width = narrow ? double.NaN : ContentWidth;
    }
}
