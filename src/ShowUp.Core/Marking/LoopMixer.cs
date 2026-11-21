namespace ShowUp.Core.Marking;

/// <summary>
/// Streams a looping 16-bit PCM buffer in chunks with the gain baked in, easing toward a target gain,
/// for heads that can only play buffers (no per-stream volume). Not thread-safe except <see cref="TargetGain"/>.
/// </summary>
public sealed class LoopMixer(short[] loop, int sampleRate, double smoothingSeconds = 0.03)
{
    private readonly double _coefficient = 1 - Math.Exp(-1 / (Math.Max(1, sampleRate) * Math.Max(1e-6, smoothingSeconds)));
    private int _position;
    private double _gain;
    private double _target;

    /// <summary>0..1; read by the audio thread on the next chunk.</summary>
    public double TargetGain
    {
        get => Volatile.Read(ref _target);
        set => Volatile.Write(ref _target, Math.Clamp(value, 0, 1));
    }

    public double CurrentGain => _gain;

    public void Fill(Span<short> chunk)
    {
        if (loop.Length == 0)
        {
            chunk.Clear();
            return;
        }

        var target = TargetGain;
        for (var i = 0; i < chunk.Length; i++)
        {
            _gain += (target - _gain) * _coefficient;
            chunk[i] = (short)Math.Clamp(Math.Round(loop[_position] * _gain), short.MinValue, short.MaxValue);
            _position = (_position + 1) % loop.Length;
        }
    }
}
