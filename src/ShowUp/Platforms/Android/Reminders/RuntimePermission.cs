using Android.App;
using Android.Content.PM;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace ShowUp.Droid.Reminders;

/// <summary>Asks for one runtime permission; <see cref="MainActivity"/> forwards the answer.</summary>
internal static class RuntimePermission
{
    private const int RequestCode = 0x5155;
    private static TaskCompletionSource<bool>? _pending;

    public static Task<bool> RequestAsync(Activity activity, string permission)
    {
        if (ContextCompat.CheckSelfPermission(activity, permission) == Permission.Granted)
        {
            return Task.FromResult(true);
        }

        _pending?.TrySetResult(false);
        _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ActivityCompat.RequestPermissions(activity, [permission], RequestCode);
        return _pending.Task;
    }

    public static void OnResult(int requestCode, Permission[] results)
    {
        if (requestCode == RequestCode)
        {
            _pending?.TrySetResult(results is [Permission.Granted, ..]);
            _pending = null;
        }
    }
}
