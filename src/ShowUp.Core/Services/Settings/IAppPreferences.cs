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

    CrossOffStyle CrossOffStyle { get; set; }

    MarkerColor MarkerColor { get; set; }

    /// <summary>Kg/km or lb/mi for logged results; seeded from the region the first time it's read.</summary>
    UnitSystem UnitSystem { get; set; }

    /// <summary>Last Pro state the store reported, so offline launches keep it.</summary>
    bool LastKnownPro { get; set; }
}
