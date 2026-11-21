namespace ShowUp.Services.Marking;

public sealed partial class MarkingFeedback
{
    private static partial void VibrateCore(TimeSpan duration)
    {
#if __ANDROID__ || __IOS__
        Windows.Phone.Devices.Notification.VibrationDevice.GetDefault()?.Vibrate(duration);
#endif
    }
}
