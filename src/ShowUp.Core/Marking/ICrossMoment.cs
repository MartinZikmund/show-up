namespace ShowUp.Core.Marking;

/// <summary>Plays the full-screen (phone) or popup (wider) celebration after a cross. Completes when dismissed.</summary>
public interface ICrossMoment
{
    Task ShowAsync(CrossMomentInfo info);

    /// <summary>
    /// "Draw it yourself": the user crosses the session off by drawing on the big square, or taps Do it for me.
    /// Completes as soon as that's decided (persist then); the caption stays up and dismisses itself.
    /// </summary>
    Task<CrossMomentResult> DrawAsync(CrossMomentDrawRequest request);
}
