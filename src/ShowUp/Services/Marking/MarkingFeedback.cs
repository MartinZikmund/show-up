using ShowUp.Core.Marking;
using ShowUp.Services.Settings;

namespace ShowUp.Services.Marking;

/// <summary>Marker squeak + haptic tick. Every failure degrades to silence: feedback must never break a cross.</summary>
public sealed partial class MarkingFeedback : IMarkingFeedback
{
    private static readonly string[] _strokeSounds = ["marker-1.wav", "marker-2.wav", "marker-3.wav"];
    private const string TickSound = "marker-tick.wav";

    private readonly IAppPreferences _preferences;
    private readonly ILogger<MarkingFeedback> _logger;
    private readonly Random _random = new();
    private bool _loggedFirstPlay;
    private bool _soundBroken;

    public MarkingFeedback(IAppPreferences preferences, ILogger<MarkingFeedback> logger)
    {
        _preferences = preferences;
        _logger = logger;
    }

    public bool SoundEnabled
    {
        get => _preferences.MarkerSoundEnabled;
        set => _preferences.MarkerSoundEnabled = value;
    }

    public bool HapticsEnabled
    {
        get => _preferences.MarkerHapticsEnabled;
        set => _preferences.MarkerHapticsEnabled = value;
    }

    public void StrokeDrawn()
    {
        Play(_strokeSounds[_random.Next(_strokeSounds.Length)]);
        Vibrate(12);
    }

    public void SessionTicked()
    {
        Play(TickSound);
        Vibrate(8);
    }

    public void CrossLanded() => Vibrate(25);

    private void Play(string sound)
    {
        if (!SoundEnabled || _soundBroken)
        {
            return;
        }

        try
        {
            var backend = PlayCore(sound);
            if (!_loggedFirstPlay)
            {
                _loggedFirstPlay = true;
                _logger.LogInformation("Marker sound {Sound} played via {Backend}", sound, backend);
            }
        }
        catch (Exception ex)
        {
            // One failure is enough to know this head can't play; stay quiet afterwards.
            _soundBroken = true;
            _logger.LogWarning(ex, "Marker sound unavailable on this head; continuing silently");
        }
    }

    private void Vibrate(int milliseconds)
    {
        if (!HapticsEnabled)
        {
            return;
        }

        try
        {
            VibrateCore(TimeSpan.FromMilliseconds(milliseconds));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Haptics unavailable");
        }
    }

    private static Uri SoundUri(string sound) => new($"ms-appx:///Assets/Sounds/{sound}");

    private static partial string PlayCore(string sound);

    private static partial void VibrateCore(TimeSpan duration);
}
