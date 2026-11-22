namespace ShowUp.Services.Dialogs;

public enum DialogChoice
{
    Primary,
    Secondary,
    Cancel,
}

public interface IConfirmationDialogService
{
    Task<ConfirmationResult> ShowAsync(string title, string text);

    /// <summary>Two actions and Cancel (the default button).</summary>
    Task<DialogChoice> ChooseAsync(string title, string text, string primary, string secondary);
}
