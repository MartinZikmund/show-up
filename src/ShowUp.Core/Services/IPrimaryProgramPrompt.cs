using ShowUp.Core.Tracking;

namespace ShowUp.Core.Services;

/// <summary>Asks a lapsed user which of their programs stays active.</summary>
public interface IPrimaryProgramPrompt
{
    /// <returns>The chosen enrollment, or null when dismissed.</returns>
    Task<Guid?> PickAsync(IReadOnlyList<ActiveEnrollment> programs, Guid suggested);
}
