#if __IOS__
using AVFoundation;
using Foundation;

namespace ShowUp.Services.Marking;

// Uno's iOS MediaPlayer forces the Playback session category, which stops the user's music.
// Ambient mixes with it and respects the silent switch, which is what a squeak should do.
public sealed partial class MarkingFeedback
{
    private static readonly Dictionary<string, AVAudioPlayer> _players = new();

    static partial void PrepareSounds(string[] sounds)
    {
        AVAudioSession.SharedInstance().SetCategory(AVAudioSessionCategory.Ambient, AVAudioSessionCategoryOptions.MixWithOthers);

        foreach (var sound in sounds)
        {
            var url = NSUrl.FromFilename(Path.Combine(NSBundle.MainBundle.BundlePath, "Assets", "Sounds", sound));
            if (AVAudioPlayer.FromUrl(url, out _) is { } player)
            {
                // Rate changes only work when enabled before PrepareToPlay.
                player.EnableRate = sound == ScratchSound;
                player.PrepareToPlay();
                _players[sound] = player;
            }
        }
    }

    // A dedicated looping player: volume and (with EnableRate) rate change live.
    static partial void StartScratchCore(string sound, ref string? backend)
    {
        if (!_players.TryGetValue(sound, out var player))
        {
            return;
        }

        player.NumberOfLoops = -1;
        player.Volume = 0;
        player.CurrentTime = 0;
        player.Play();
        backend = "AVAudioPlayer loop";
    }

    static partial void SetScratchCore(double volume, double rate)
    {
        if (_players.TryGetValue(ScratchSound, out var player))
        {
            player.Volume = (float)volume;
            player.Rate = (float)Math.Clamp(rate, 0.5, 2);
        }
    }

    static partial void StopScratchCore()
    {
        if (_players.TryGetValue(ScratchSound, out var player))
        {
            player.Stop();
        }
    }

    private static partial string PlayCore(string sound)
    {
        if (!_players.TryGetValue(sound, out var player))
        {
            throw new InvalidOperationException($"Sound {sound} could not be loaded.");
        }

        player.CurrentTime = 0;
        player.Play();
        return nameof(AVAudioPlayer);
    }
}
#endif
