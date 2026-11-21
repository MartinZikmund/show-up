namespace ShowUp.Views;

public sealed partial class ProgramNoteDialog : ContentDialog
{
    public ProgramNoteDialog(string programName, string note, bool isReadOnly = false)
    {
        ProgramName = programName;
        InitializeComponent();
        NoteBox.Text = note;
        JournalDialogLayout.Apply(this, ContentPanel);
        if (isReadOnly)
        {
            NoteBox.IsReadOnly = true;
            PrimaryButtonText = "";
            CloseButtonText = ShowUp.Services.Localization.Localizer.Instance["Close"];
        }
    }

    public string ProgramName { get; }

    public string Note => NoteBox.Text;
}
