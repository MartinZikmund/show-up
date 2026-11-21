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

    // Scratch loop: winmm waveOut plays one looping buffer; waveOutSetVolume follows the pen.
    // Rate isn't adjustable on most drivers, so pitch stays put on this head.
    private const uint WaveMapper = uint.MaxValue;
    private const uint WhdrBeginLoop = 0x4;
    private const uint WhdrEndLoop = 0x8;
    private const uint FullVolume = 0xFFFFFFFF;

    private static IntPtr _scratchPcm;
    private static int _scratchPcmLength;
    private static WaveFormat _scratchFormat;
    private static IntPtr _waveOut;
    private static IntPtr _waveHeader;

    static partial void StartScratchCore(string sound, ref string? backend)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (_scratchPcm == IntPtr.Zero)
        {
            (_scratchFormat, var pcm) = ReadWav(Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", sound));
            _scratchPcm = Marshal.AllocHGlobal(pcm.Length);
            Marshal.Copy(pcm, 0, _scratchPcm, pcm.Length);
            _scratchPcmLength = pcm.Length;
        }

        Check(waveOutOpen(out _waveOut, WaveMapper, ref _scratchFormat, IntPtr.Zero, IntPtr.Zero, 0), nameof(waveOutOpen));
        Check(waveOutSetVolume(_waveOut, 0), nameof(waveOutSetVolume));

        var size = Marshal.SizeOf<WaveHeader>();
        _waveHeader = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(new WaveHeader { Data = _scratchPcm, BufferLength = (uint)_scratchPcmLength }, _waveHeader, false);
        Check(waveOutPrepareHeader(_waveOut, _waveHeader, (uint)size), nameof(waveOutPrepareHeader));

        // Loop flags go on after prepare (it wants dwFlags zero); ~an hour of loops is "until stopped".
        var header = Marshal.PtrToStructure<WaveHeader>(_waveHeader);
        header.Flags |= WhdrBeginLoop | WhdrEndLoop;
        header.Loops = 3600;
        Marshal.StructureToPtr(header, _waveHeader, false);
        Check(waveOutWrite(_waveOut, _waveHeader, (uint)size), nameof(waveOutWrite));
        backend = "winmm waveOut loop";
    }

    static partial void SetScratchCore(double volume, double rate)
    {
        if (_waveOut != IntPtr.Zero)
        {
            var level = (uint)(Math.Clamp(volume, 0, 1) * 0xFFFF);
            waveOutSetVolume(_waveOut, level | level << 16);
        }
    }

    static partial void StopScratchCore()
    {
        if (_waveOut == IntPtr.Zero)
        {
            return;
        }

        // On Vista+ the volume is the app's session volume: restore it so the squeaks stay audible.
        waveOutSetVolume(_waveOut, FullVolume);
        waveOutReset(_waveOut);
        waveOutUnprepareHeader(_waveOut, _waveHeader, (uint)Marshal.SizeOf<WaveHeader>());
        waveOutClose(_waveOut);
        Marshal.FreeHGlobal(_waveHeader);
        _waveOut = IntPtr.Zero;
        _waveHeader = IntPtr.Zero;
    }

    private static void Check(uint result, string call)
    {
        if (result != 0)
        {
            StopScratchCore();
            throw new InvalidOperationException($"{call} failed ({result}).");
        }
    }

    // Minimal RIFF reader for the generator's own 16-bit PCM files.
    private static (WaveFormat Format, byte[] Pcm) ReadWav(string path)
    {
        using BinaryReader reader = new(File.OpenRead(path));
        reader.ReadBytes(12);
        WaveFormat? format = null;
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var length = reader.ReadInt32();
            if (id == "fmt ")
            {
                format = new WaveFormat
                {
                    FormatTag = reader.ReadUInt16(),
                    Channels = reader.ReadUInt16(),
                    SamplesPerSec = reader.ReadUInt32(),
                    AvgBytesPerSec = reader.ReadUInt32(),
                    BlockAlign = reader.ReadUInt16(),
                    BitsPerSample = reader.ReadUInt16(),
                };
                reader.ReadBytes(length - 16);
            }
            else if (id == "data" && format is { } f)
            {
                return (f, reader.ReadBytes(length));
            }
            else
            {
                reader.ReadBytes(length);
            }
        }

        throw new InvalidDataException($"{path} has no PCM data.");
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public IntPtr User;
        public uint Flags;
        public uint Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [DllImport("winmm.dll")]
    private static extern uint waveOutOpen(out IntPtr waveOut, uint deviceId, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    private static extern uint waveOutPrepareHeader(IntPtr waveOut, IntPtr header, uint size);

    [DllImport("winmm.dll")]
    private static extern uint waveOutUnprepareHeader(IntPtr waveOut, IntPtr header, uint size);

    [DllImport("winmm.dll")]
    private static extern uint waveOutWrite(IntPtr waveOut, IntPtr header, uint size);

    [DllImport("winmm.dll")]
    private static extern uint waveOutSetVolume(IntPtr waveOut, uint volume);

    [DllImport("winmm.dll")]
    private static extern uint waveOutReset(IntPtr waveOut);

    [DllImport("winmm.dll")]
    private static extern uint waveOutClose(IntPtr waveOut);
}
#endif
