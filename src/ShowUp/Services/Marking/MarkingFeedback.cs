using ShowUp.Core.Marking;
using ShowUp.Services.Settings;

namespace ShowUp.Services.Marking;

/// <summary>Marker squeak + haptic tick. Every failure degrades to silence: feedback must never break a cross.</summary>
public sealed partial class MarkingFeedback : IMarkingFeedback
{
    private static readonly string[] _strokeSounds = ["marker_1.wav", "marker_2.wav", "marker_3.wav"];
    private const string TickSound = "marker_tick.wav";
    private const string ScratchSound = "marker_scratch.wav";

    private readonly IAppPreferences _preferences;
    private readonly ILogger<MarkingFeedback> _logger;
    private readonly Random _random = new();
    private bool _loggedFirstPlay;
    private bool _soundBroken;
    private bool _scratchBroken;
    private bool _scratching;

    public MarkingFeedback(IAppPreferences preferences, ILogger<MarkingFeedback> logger)
    {
        _preferences = preferences;
        _logger = logger;

        try
        {
            PrepareSounds([.. _strokeSounds, TickSound, ScratchSound]);
        }
        catch (Exception ex)
        {
            _soundBroken = true;
            _logger.LogWarning(ex, "Marker sounds could not be prepared; continuing silently");
        }
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

    public void ScratchStart()
    {
        Vibrate(8);
        ScratchStop();
        if (!SoundEnabled || _soundBroken)
        {
            return;
        }

        string? backend = null;
        if (!_scratchBroken)
        {
            try
            {
                StartScratchCore(ScratchSound, ref backend);
            }
            catch (Exception ex)
            {
                _scratchBroken = true;
                backend = null;
                _logger.LogWarning(ex, "Scratch loop unavailable; falling back to a stroke sound");
            }
        }

        if (backend is null)
        {
            // Heads without a loop with live gain get the short squeak per stroke instead.
            Play(_strokeSounds[_random.Next(_strokeSounds.Length)]);
            return;
        }

        if (!_scratching)
        {
            _logger.LogDebug("Scratch loop via {Backend}", backend);
        }

        _scratching = true;
    }

    public void ScratchSpeed(double normalizedSpeed)
    {
        if (!_scratching)
        {
            return;
        }

        var mix = ScratchMix.From(normalizedSpeed);
        TryScratch(() => SetScratchCore(mix.Volume, mix.Rate));
    }

    public void ScratchStop()
    {
        if (!_scratching)
        {
            return;
        }

        _scratching = false;
        TryScratch(StopScratchCore);
    }

    public void DayTaken()
    {
        Play(_strokeSounds[_random.Next(_strokeSounds.Length)]);
        Vibrate(30);
    }

    private void TryScratch(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _scratchBroken = true;
            _scratching = false;
            _logger.LogWarning(ex, "Scratch loop failed; continuing without it");
        }
    }

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

    /// <summary>Optional per-head preload, so the first stroke isn't silent where loading is asynchronous.</summary>
    static partial void PrepareSounds(string[] sounds);

    private static partial string PlayCore(string sound);

    private static partial void VibrateCore(TimeSpan duration);

    /// <summary>Starts the silent scratch loop; leave <paramref name="backend"/> null where looping with live gain isn't possible.</summary>
    static partial void StartScratchCore(string sound, ref string? backend);

    /// <param name="volume">0..1.</param>
    /// <param name="rate">Playback rate, ~0.75..1.67 (pitch follows speed); heads that can't change it ignore it.</param>
    static partial void SetScratchCore(double volume, double rate);

    static partial void StopScratchCore();
}
