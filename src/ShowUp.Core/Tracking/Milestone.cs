namespace ShowUp.Core.Tracking;

public enum MilestoneKind
{
    Week,
    Phase,
    Program,
}

/// <summary>What a cross just completed: the largest of the week, the phase and the program it finished.</summary>
/// <param name="WeekNumber">1-based week across the track, of the crossed day.</param>
public sealed record Milestone(MilestoneKind Kind, string PhaseName, int WeekNumber);
