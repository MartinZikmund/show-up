namespace ShowUp.Views;

public sealed partial class ProgramNoteDialog : ContentDialog
{
    public ProgramNoteDialog(string programName, string note)
    {
        ProgramName = programName;
        InitializeComponent();
        NoteBox.Text = note;
        JournalDialogLayout.Apply(this, ContentPanel);
    }

    public string ProgramName { get; }

    public string Note => NoteBox.Text;
}
