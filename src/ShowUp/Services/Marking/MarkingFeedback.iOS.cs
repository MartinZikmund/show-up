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
                player.PrepareToPlay();
                _players[sound] = player;
            }
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
