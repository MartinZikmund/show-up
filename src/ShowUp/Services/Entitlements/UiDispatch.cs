using Microsoft.UI.Dispatching;

namespace ShowUp.Services.Entitlements;

/// <summary>Store callbacks arrive on pool threads; the UI listens to <c>Changed</c> and must hear it on its own thread.</summary>
internal static class UiDispatch
{
    public static void Run(DispatcherQueue? queue, Action action)
    {
        if (queue is null || queue.HasThreadAccess)
        {
            action();
        }
        else
        {
            queue.TryEnqueue(() => action());
        }
    }
}
