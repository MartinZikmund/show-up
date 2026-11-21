#if __DESKTOP__
using System.Runtime.InteropServices;
using ShowUp.Core.Marking;

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

    // Scratch loop: winmm waveOut fed with short chunks that have the gain baked in (LoopMixer), so the
    // pen speed never touches the app's session volume (waveOutSetVolume would). Pitch stays put here.
    private static short[]? _scratchSamples;
    private static WaveFormat _scratchFormat;
    private static ScratchStream? _scratch;

    static partial void StartScratchCore(string sound, ref string? backend)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (_scratchSamples is null)
        {
            (_scratchFormat, var pcm) = ReadWav(Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", sound));
            if (_scratchFormat is not { FormatTag: 1, Channels: 1, BitsPerSample: 16 })
            {
                throw new InvalidDataException($"{sound} must be 16-bit mono PCM.");
            }

            _scratchSamples = new short[pcm.Length / 2];
            Buffer.BlockCopy(pcm, 0, _scratchSamples, 0, _scratchSamples.Length * 2);
        }

        _scratch = new ScratchStream(_scratchFormat, new LoopMixer(_scratchSamples, (int)_scratchFormat.SamplesPerSec));
        backend = "winmm waveOut stream";
    }

    static partial void SetScratchCore(double volume, double rate)
    {
        if (_scratch is { } scratch)
        {
            scratch.Mixer.TargetGain = volume;
        }
    }

    static partial void StopScratchCore()
    {
        _scratch?.Dispose();
        _scratch = null;
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

    /// <summary>A few 25 ms buffers in flight; a background thread refills each one the device has finished.</summary>
    private sealed class ScratchStream : IDisposable
    {
        private const int BufferCount = 4;
        private const int ChunkMilliseconds = 25;
        private const uint WaveMapper = uint.MaxValue;
        private const uint WhdrDone = 0x1;

        private static readonly int _headerSize = Marshal.SizeOf<WaveHeader>();
        private static readonly int _flagsOffset = (int)Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.Flags));

        private readonly IntPtr _waveOut;
        private readonly IntPtr[] _headers = new IntPtr[BufferCount];
        private readonly IntPtr[] _data = new IntPtr[BufferCount];
        private readonly short[] _chunk;
        private readonly Thread _pump;
        private volatile bool _running = true;

        public ScratchStream(WaveFormat format, LoopMixer mixer)
        {
            Mixer = mixer;
            _chunk = new short[(int)(format.SamplesPerSec * ChunkMilliseconds / 1000)];
            Check(waveOutOpen(out _waveOut, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0), nameof(waveOutOpen));
            try
            {
                for (var i = 0; i < BufferCount; i++)
                {
                    _data[i] = Marshal.AllocHGlobal(_chunk.Length * 2);
                    _headers[i] = Marshal.AllocHGlobal(_headerSize);
                    Marshal.StructureToPtr(new WaveHeader { Data = _data[i], BufferLength = (uint)(_chunk.Length * 2) }, _headers[i], false);
                    Check(waveOutPrepareHeader(_waveOut, _headers[i], (uint)_headerSize), nameof(waveOutPrepareHeader));
                    Queue(i);
                }
            }
            catch
            {
                Close();
                throw;
            }

            _pump = new Thread(Pump) { IsBackground = true, Name = "Marker scratch", Priority = ThreadPriority.AboveNormal };
            _pump.Start();
        }

        public LoopMixer Mixer { get; }

        public void Dispose()
        {
            _running = false;
            _pump.Join();
            Close();
        }

        private void Pump()
        {
            try
            {
                while (_running)
                {
                    for (var i = 0; i < BufferCount && _running; i++)
                    {
                        if ((Marshal.ReadInt32(_headers[i], _flagsOffset) & WhdrDone) != 0)
                        {
                            Queue(i);
                        }
                    }

                    Thread.Sleep(5);
                }
            }
            catch
            {
                // An exception on this thread would end the app; a silent scratch is the right failure.
                _running = false;
            }
        }

        private void Queue(int index)
        {
            Mixer.Fill(_chunk);
            Marshal.Copy(_chunk, 0, _data[index], _chunk.Length);
            waveOutWrite(_waveOut, _headers[index], (uint)_headerSize);
        }

        private void Close()
        {
            if (_waveOut != IntPtr.Zero)
            {
                waveOutReset(_waveOut);
            }

            for (var i = 0; i < BufferCount; i++)
            {
                if (_headers[i] != IntPtr.Zero)
                {
                    waveOutUnprepareHeader(_waveOut, _headers[i], (uint)_headerSize);
                    Marshal.FreeHGlobal(_headers[i]);
                    _headers[i] = IntPtr.Zero;
                }

                if (_data[i] != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_data[i]);
                    _data[i] = IntPtr.Zero;
                }
            }

            if (_waveOut != IntPtr.Zero)
            {
                waveOutClose(_waveOut);
            }
        }

        private static void Check(uint result, string call)
        {
            if (result != 0)
            {
                throw new InvalidOperationException($"{call} failed ({result}).");
            }
        }
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
    private static extern uint waveOutReset(IntPtr waveOut);

    [DllImport("winmm.dll")]
    private static extern uint waveOutClose(IntPtr waveOut);
}
#endif
