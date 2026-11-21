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

    private static int _scratchStream;

    // SoundPool loops (-1) with per-stream volume and rate (0.5-2), so it can follow the pen.
    static partial void StartScratchCore(string sound, ref string? backend)
    {
        if (_pool is null || !_soundIds.TryGetValue(sound, out var id))
        {
            return;
        }

        lock (_loadedIds)
        {
            if (!_loadedIds.Contains(id))
            {
                return;
            }
        }

        _scratchStream = _pool.Play(id, 0, 0, 2, -1, 1);
        if (_scratchStream != 0)
        {
            backend = "SoundPool loop";
        }
    }

    static partial void SetScratchCore(double volume, double rate)
    {
        if (_pool is not null && _scratchStream != 0)
        {
            _pool.SetVolume(_scratchStream, (float)volume, (float)volume);
            _pool.SetRate(_scratchStream, (float)Math.Clamp(rate, 0.5, 2));
        }
    }

    static partial void StopScratchCore()
    {
        if (_pool is not null && _scratchStream != 0)
        {
            _pool.Stop(_scratchStream);
            _scratchStream = 0;
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
