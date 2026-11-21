#if __DESKTOP__
using System.Runtime.InteropServices;

namespace ShowUp.Services.Marking;

// Uno's desktop MediaPlayer needs the MediaPlayerElement feature, which ships LibVLC (hundreds of MB)
// for a 16 KB squeak. winmm's PlaySound does the job on Windows; Linux/macOS desktop stay silent.
public sealed partial class MarkingFeedback
{
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndMemory = 0x0004;

    private static readonly Dictionary<string, IntPtr> _buffers = new();

    private static partial string PlayCore(string sound)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "none (desktop audio is Windows-only)";
        }

        if (!_buffers.TryGetValue(sound, out var buffer))
        {
            buffer = LoadUnmanaged(sound);
            _buffers[sound] = buffer;
        }

        // SND_MEMORY + SND_ASYNC reads the buffer after returning, so it lives in unmanaged memory forever.
        if (!PlaySound(buffer, IntPtr.Zero, SndAsync | SndNoDefault | SndMemory))
        {
            throw new InvalidOperationException("PlaySound failed.");
        }

        return "winmm PlaySound";
    }

    private static IntPtr LoadUnmanaged(string sound)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", sound);
        var bytes = File.ReadAllBytes(path);
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, buffer, bytes.Length);
        return buffer;
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", SetLastError = true)]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
}
#endif
