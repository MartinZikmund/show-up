namespace ShowUp.Core.Marking;

/// <summary>
/// Pen speed to scratch loop settings, after the prototype's <c>scratchSpeed</c>: gain <c>min(.32, v * .9)</c>
/// and band <c>1800 + min(2200, v * 4000)</c> Hz, expressed against the loop's 2400 Hz band and 0.32 peak.
/// </summary>
public readonly record struct ScratchMix(double Volume, double Rate)
{
    private const double PeakGain = 0.32;
    private const double LoopBand = 2400;

    public static ScratchMix From(double normalizedSpeed)
    {
        var v = double.IsFinite(normalizedSpeed) ? Math.Max(0, normalizedSpeed) : 0;
        return new(Math.Min(PeakGain, v * 0.9) / PeakGain, (1800 + Math.Min(2200, v * 4000)) / LoopBand);
    }
}
