using ShowUp.Core.Marking;

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
}
