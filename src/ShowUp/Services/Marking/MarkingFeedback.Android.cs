#if __ANDROID__
using Android.Media;

namespace ShowUp.Services.Marking;

// SoundPool with sonification attributes: no audio focus request, so workout music keeps playing at full volume.
public sealed partial class MarkingFeedback
{
    private const string SoundAssetFolder = "Assets/Sounds/";

    private static readonly Dictionary<string, int> _soundIds = new();
    private static readonly HashSet<int> _loadedIds = [];
    private static SoundPool? _pool;

    static partial void PrepareSounds(string[] sounds)
    {
        if (_pool is not null)
        {
            return;
        }

        var attributes = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.AssistanceSonification)!
            .SetContentType(AudioContentType.Sonification)!
            .Build();
        _pool = new SoundPool.Builder().SetMaxStreams(4)!.SetAudioAttributes(attributes)!.Build()!;
        _pool.LoadComplete += (_, e) =>
        {
            if (e.Status == 0)
            {
                lock (_loadedIds)
                {
                    _loadedIds.Add(e.SampleId);
                }
            }
        };

        var assets = Android.App.Application.Context.Assets!;
        foreach (var sound in sounds)
        {
            using var descriptor = assets.OpenFd(SoundAssetFolder + sound);
            _soundIds[sound] = _pool.Load(descriptor, 1);
        }
    }

    private static partial string PlayCore(string sound)
    {
        if (_pool is null || !_soundIds.TryGetValue(sound, out var id))
        {
            throw new InvalidOperationException($"Sound {sound} was not prepared.");
        }

        lock (_loadedIds)
        {
            if (!_loadedIds.Contains(id))
            {
                return "SoundPool (still loading, skipped)";
            }
        }

        _pool.Play(id, 1, 1, 1, 0, 1);
        return nameof(SoundPool);
    }
}
#endif
