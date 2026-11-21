#if !__DESKTOP__ && !__ANDROID__ && !__IOS__
using Windows.Media.Core;
using Windows.Media.Playback;

namespace ShowUp.Services.Marking;

// Windows and WASM: Windows.Media.Playback ships with the head's runtime, no UnoFeature needed.
public sealed partial class MarkingFeedback
{
    private static readonly Dictionary<string, MediaPlayer> _players = new();

    static partial void PrepareSounds(string[] sounds)
    {
        foreach (var sound in sounds)
        {
#if __WASM__
            if (sound == ScratchSound)
            {
                continue;
            }
#endif
            GetPlayer(sound);
        }
    }

#if !__WASM__
    // Windows: a looping MediaPlayer with live Volume and PlaybackRate. (WebAssembly uses Web Audio, see .Wasm.cs.)
    static partial void StartScratchCore(string sound, ref string? backend)
    {
        var player = GetPlayer(sound);
        player.IsLoopingEnabled = true;
        player.Volume = 0;
        player.Play();
        backend = "MediaPlayer loop";
    }

    static partial void SetScratchCore(double volume, double rate)
    {
        var player = GetPlayer(ScratchSound);
        player.Volume = volume;
        player.PlaybackSession.PlaybackRate = rate;
    }

    static partial void StopScratchCore() => GetPlayer(ScratchSound).Pause();
#endif

    private static partial string PlayCore(string sound)
    {
        var player = GetPlayer(sound);

        // Restart instead of queueing; never seek while Opening (Uno kills the player, unoplatform/uno#26158).
        if (player.PlaybackSession.PlaybackState != MediaPlaybackState.Opening)
        {
            player.PlaybackSession.Position = TimeSpan.Zero;
        }

        player.Play();
        return nameof(MediaPlayer);
    }

    private static MediaPlayer GetPlayer(string sound)
    {
        if (!_players.TryGetValue(sound, out var player))
        {
            player = new MediaPlayer
            {
                // A sound effect mixes with the user's music instead of taking over the audio session.
                AudioCategory = MediaPlayerAudioCategory.SoundEffects,
                Source = MediaSource.CreateFromUri(SoundUri(sound)),
                AutoPlay = false,
            };
            _players[sound] = player;
        }

        return player;
    }
}
#endif
