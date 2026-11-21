namespace ShowUp.Services.Dialogs;

public interface IDialogCoordinator
{
    Task<ContentDialogResult> ShowAsync(ContentDialog dialog);
}
