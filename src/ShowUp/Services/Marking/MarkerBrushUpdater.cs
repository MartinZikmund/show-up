using CommunityToolkit.Mvvm.Messaging;
using ShowUp.Core.Marking;
using ShowUp.Core.Messages;
using ShowUp.Services.Settings;

namespace ShowUp.Services.Marking;

/// <summary>
/// Paints the chosen marker color into the app's <c>MarkerBrush</c> and <c>MarkerBleedBrush</c> in the Light and
/// Dark theme dictionaries. Every consumer holds those same brush instances, so they all repaint at once.
/// High Contrast keeps its system colors.
/// </summary>
public sealed class MarkerBrushUpdater : IRecipient<MarkerColorChangedMessage>
{
    private readonly IAppPreferences _preferences;

    public MarkerBrushUpdater(IAppPreferences preferences, IMessenger messenger)
    {
        _preferences = preferences;
        messenger.Register(this);
    }

    public void Apply() => Apply(_preferences.MarkerColor);

    public void Receive(MarkerColorChangedMessage message) => Apply(message.Color);

    private static void Apply(MarkerColor color)
    {
        var (light, dark) = MarkerPalette.Resolve(color);
        var (lightBleed, darkBleed) = MarkerPalette.ResolveBleed(color);
        Set(Application.Current.Resources, "Light", "MarkerBrush", light);
        Set(Application.Current.Resources, "Light", "MarkerBleedBrush", lightBleed);
        Set(Application.Current.Resources, "Dark", "MarkerBrush", dark);
        Set(Application.Current.Resources, "Dark", "MarkerBleedBrush", darkBleed);
    }

    private static bool Set(ResourceDictionary dictionary, string theme, string key, Windows.UI.Color color)
    {
        if (dictionary.ThemeDictionaries.TryGetValue(theme, out var themed)
            && themed is ResourceDictionary themeDictionary
            && themeDictionary.TryGetValue(key, out var brush)
            && brush is SolidColorBrush solid)
        {
            solid.Color = color;
            return true;
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (Set(merged, theme, key, color))
            {
                return true;
            }
        }

        return false;
    }
}
