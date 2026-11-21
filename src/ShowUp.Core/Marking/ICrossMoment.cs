namespace ShowUp.Core.Marking;

/// <summary>Plays the full-screen (phone) or popup (wider) celebration after a cross. Completes when dismissed.</summary>
public interface ICrossMoment
{
    Task ShowAsync(CrossMomentInfo info);
}
