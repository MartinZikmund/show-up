#if !__DESKTOP__
using Windows.Media.Core;
using Windows.Media.Playback;
using Player = Windows.Media.Playback.MediaPlayer;

namespace ShowUp.Services.Marking;

// Windows, Android, iOS and WASM: Windows.Media.Playback ships with the head's runtime, no UnoFeature needed.
public sealed partial class MarkingFeedback
{
    private static readonly Dictionary<string, Player> _players = new();

    private static partial string PlayCore(string sound)
    {
        if (!_players.TryGetValue(sound, out var player))
        {
            player = new Player
            {
                Source = MediaSource.CreateFromUri(SoundUri(sound)),
                AutoPlay = false,
            };
            _players[sound] = player;
        }

        // Restart instead of queueing: a stroke that starts mid-squeak should squeak from the top.
        // Never seek while Opening: Uno Android kills the player for good (unoplatform/uno#26158).
        if (player.PlaybackSession.PlaybackState != MediaPlaybackState.Opening)
        {
            player.PlaybackSession.Position = TimeSpan.Zero;
        }

        player.Play();
        return "MediaPlayer";
    }
}
#endif
