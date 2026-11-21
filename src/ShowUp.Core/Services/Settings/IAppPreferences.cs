using ShowUp.Core.Marking;
using ShowUp.Core.Tracking;

namespace ShowUp.Services.Settings;

public interface IAppPreferences
{
    int DataVersion { get; set; }

    bool FirstStart { get; set; }

    int LaunchCount { get; set; }

    bool OfferUserRating { get; set; }

    ElementTheme Theme { get; set; }

    bool MarkerSoundEnabled { get; set; }

    bool MarkerHapticsEnabled { get; set; }

    CrossMomentMode CrossMomentMode { get; set; }

    /// <summary>Synced: stored through <c>SyncedPersonalization</c>, not per device.</summary>
    CrossOffStyle CrossOffStyle { get; set; }

    /// <summary>Synced.</summary>
    MarkerColor MarkerColor { get; set; }

    /// <summary>Synced. Kg/km or lb/mi for logged results; seeded from the region the first time it's read.</summary>
    UnitSystem UnitSystem { get; set; }

    /// <summary>Points <see cref="LastKnownPro"/> and <see cref="LastKnownProExpires"/> at the user's own cache (null: anonymous).</summary>
    void UseProAccount(string? userId);

    /// <summary>Last Pro state the store reported, so offline launches keep it.</summary>
    bool LastKnownPro { get; set; }

    /// <summary>When the cached Pro state lapses; null for lifetime or unknown.</summary>
    DateTimeOffset? LastKnownProExpires { get; set; }

    /// <summary>The program this device's Calendar and Results show (per device, not synced).</summary>
    Guid? SelectedEnrollmentId { get; set; }
}
