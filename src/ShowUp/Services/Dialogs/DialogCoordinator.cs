namespace ShowUp.Services.Dialogs;

public sealed class DialogCoordinator : IDialogCoordinator
{
    private readonly Queue<QueuedDialog> _dialogQueue = new();
    private bool _isShowingDialog;

    public async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        var queuedDialog = new QueuedDialog(dialog);
        _dialogQueue.Enqueue(queuedDialog);
        await ProcessQueueAsync();
        return await queuedDialog.CompletionSource.Task;
    }

    private async Task ProcessQueueAsync()
    {
        if (_isShowingDialog)
        {
            return;
        }

        _isShowingDialog = true;
        while (_dialogQueue.Count > 0)
        {
            var queued = _dialogQueue.Dequeue();
            try
            {
                ApplyAppTheme(queued.Dialog);
                var result = await queued.Dialog.ShowAsync();
                queued.CompletionSource.SetResult(result);
            }
            catch (Exception ex)
            {
                queued.CompletionSource.SetException(ex);
            }
        }

        _isShowingDialog = false;
    }

    // A dialog lives in the popup layer and can come up in the system theme while the app is on the other one.
    private static void ApplyAppTheme(ContentDialog dialog)
    {
        if (dialog.XamlRoot?.Content is FrameworkElement root)
        {
            dialog.RequestedTheme = root.ActualTheme;
        }
    }

    private sealed class QueuedDialog
    {
        public QueuedDialog(ContentDialog dialog)
        {
            Dialog = dialog;
        }

        public TaskCompletionSource<ContentDialogResult> CompletionSource { get; } = new();

        public ContentDialog Dialog { get; }
    }
}
