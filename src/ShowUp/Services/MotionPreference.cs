using Windows.UI.ViewManagement;

namespace ShowUp.Services;

public sealed class MotionPreference : IMotionPreference
{
    private readonly Lazy<UISettings?> _settings = new(Create);

    public bool AnimationsEnabled => _settings.Value?.AnimationsEnabled ?? true;

    // Not every head can create UISettings; with none, animate.
    private static UISettings? Create()
    {
        try
        {
            return new UISettings();
        }
        catch
        {
            return null;
        }
    }
}
