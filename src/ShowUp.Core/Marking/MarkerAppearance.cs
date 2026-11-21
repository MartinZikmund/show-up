using ShowUp.Core.Tracking;

namespace ShowUp.Core.Marking;

/// <summary>
/// Generated strokes for a day where some sessions may be drawn by hand. Drawn sessions show only their ink;
/// a complete day with any auto-crossed session gets the full X; a partial day applies the stroke rule to its
/// auto-crossed sessions alone (two required, one auto = "/").
/// </summary>
public static class MarkerAppearance
{
    /// <summary>The value for <c>MarkerCrossControl.Strokes</c>; drawn sessions go in its <c>Inks</c>.</summary>
    public static int StrokesFor(DayStatus status, IReadOnlySet<int> autoCrossedSessions)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(autoCrossedSessions);

        var autos = status.CompletedSessions.Where(autoCrossedSessions.Contains).ToList();
        if (status.Progress == DayProgress.Complete)
        {
            return autos.Count > 0 ? 2 : 0;
        }

        var requiredAutos = autos.Count(i => !status.OptionalSessions.Contains(i));
        return status.RequiredCount == 2 && requiredAutos == 1 ? 1 : 0;
    }

    /// <summary>
    /// Strokes before crossing <paramref name="sessionIndex"/>, and after it for either outcome of the draw moment:
    /// "Do it for me" (the session counts as auto) or drawn (ink only, but it can still complete a day that has autos).
    /// </summary>
    public static (int Before, int AfterAuto, int AfterDrawn) Outcomes(DayStatus before, IReadOnlySet<int> autoCrossedSessions, int sessionIndex)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(autoCrossedSessions);

        var after = before with { CompletedSessions = new HashSet<int>(before.CompletedSessions) { sessionIndex } };
        var autosAfter = new HashSet<int>(autoCrossedSessions) { sessionIndex };
        return (StrokesFor(before, autoCrossedSessions), StrokesFor(after, autosAfter), StrokesFor(after, autoCrossedSessions));
    }
}
