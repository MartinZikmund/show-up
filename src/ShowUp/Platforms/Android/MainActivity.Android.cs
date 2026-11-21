using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using CommunityToolkit.Mvvm.Messaging;

namespace ShowUp.Droid;

[Activity(
    MainLauncher = true,
    ConfigurationChanges = global::Uno.UI.ActivityHelper.AllConfigChanges,
    WindowSoftInputMode = SoftInput.AdjustNothing | SoftInput.StateHidden
)]
public class MainActivity : Uno.UI.Runtime.Android.ApplicationActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        global::AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);

        base.OnCreate(savedInstanceState);
    }

    // A reminder tapped while the app is running; a cold start lands on Today anyway.
    protected override void OnNewIntent(Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent?.GetStringExtra(Reminders.ReminderAlarms.ExtraOpen) == "today")
        {
            WeakReferenceMessenger.Default.Send(new ShowUp.Core.Messages.ReminderOpenedMessage());
        }
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        Reminders.RuntimePermission.OnResult(requestCode, grantResults);
    }

}
