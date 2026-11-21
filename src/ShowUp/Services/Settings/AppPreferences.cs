using MZikmund.Toolkit.WinUI.Services;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Marking;
using ShowUp.Core.Tracking;

namespace ShowUp.Services.Settings;

public sealed class AppPreferences : IAppPreferences
{
    private readonly IPreferences _preferences;
    private readonly AccountProCache _proCache;

    public AppPreferences(IPreferences preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _proCache = new(preferences);
    }

    private const string DataVersionKey = "AppDataVersion";
    public int DataVersion
    {
        get => _preferences.Get(DataVersionKey, 0);
        set => _preferences.Set(DataVersionKey, value);
    }

    private const string FirstStartKey = "AppFirstStart";
    public bool FirstStart
    {
        get => _preferences.Get(FirstStartKey, true);
        set => _preferences.Set(FirstStartKey, value);
    }

    private const string LaunchCountKey = "AppLaunchCount";
    public int LaunchCount
    {
        get => _preferences.Get(LaunchCountKey, 0);
        set => _preferences.Set(LaunchCountKey, value);
    }

    private const string OfferUserRatingKey = "OfferUserRating";
    public bool OfferUserRating
    {
        get => _preferences.Get(OfferUserRatingKey, true);
        set => _preferences.Set(OfferUserRatingKey, value);
    }

    private const string ThemeKey = "AppTheme";
    public ElementTheme Theme
    {
        get => _preferences.GetComplex(ThemeKey, ElementTheme.Default);
        set => _preferences.SetComplex(ThemeKey, value);
    }

    private const string MarkerSoundEnabledKey = "MarkerSoundEnabled";
    public bool MarkerSoundEnabled
    {
        get => _preferences.Get(MarkerSoundEnabledKey, true);
        set => _preferences.Set(MarkerSoundEnabledKey, value);
    }

    private const string MarkerHapticsEnabledKey = "MarkerHapticsEnabled";
    public bool MarkerHapticsEnabled
    {
        get => _preferences.Get(MarkerHapticsEnabledKey, true);
        set => _preferences.Set(MarkerHapticsEnabledKey, value);
    }

    // Stored as int: enum values round-trip through ApplicationData on every head.
    private const string CrossMomentModeKey = "CrossMomentMode";
    public CrossMomentMode CrossMomentMode
    {
        get => _preferences.Get(CrossMomentModeKey, (int)CrossMomentMode.OnlyToday) is var raw && Enum.IsDefined((CrossMomentMode)raw)
            ? (CrossMomentMode)raw
            : CrossMomentMode.OnlyToday;
        set => _preferences.Set(CrossMomentModeKey, (int)value);
    }

    private const string CrossOffStyleKey = "CrossOffStyle";
    public CrossOffStyle CrossOffStyle
    {
        get => _preferences.Get(CrossOffStyleKey, (int)CrossOffStyle.DrawYourself) is var raw && Enum.IsDefined((CrossOffStyle)raw)
            ? (CrossOffStyle)raw
            : CrossOffStyle.DrawYourself;
        set => _preferences.Set(CrossOffStyleKey, (int)value);
    }

    private const string MarkerColorKey = "MarkerColor";
    public MarkerColor MarkerColor
    {
        get => _preferences.Get(MarkerColorKey, (int)MarkerColor.Auto) is var raw && Enum.IsDefined((MarkerColor)raw)
            ? (MarkerColor)raw
            : MarkerColor.Auto;
        set => _preferences.Set(MarkerColorKey, (int)value);
    }

    public void UseProAccount(string? userId) => _proCache.UseAccount(userId);

    public bool LastKnownPro
    {
        get => _proCache.LastKnownPro;
        set => _proCache.LastKnownPro = value;
    }

    public DateTimeOffset? LastKnownProExpires
    {
        get => _proCache.LastKnownProExpires;
        set => _proCache.LastKnownProExpires = value;
    }

    private const string SelectedEnrollmentIdKey = "SelectedEnrollmentId";
    public Guid? SelectedEnrollmentId
    {
        get => Guid.TryParse(_preferences.Get(SelectedEnrollmentIdKey, ""), out var id) ? id : null;
        set => _preferences.Set(SelectedEnrollmentIdKey, value?.ToString("D") ?? "");
    }

    private const string UnitSystemKey = "UnitSystem";
    public UnitSystem UnitSystem
    {
        get
        {
            if (_preferences.Get(UnitSystemKey, -1) is var raw && Enum.IsDefined((UnitSystem)raw))
            {
                return (UnitSystem)raw;
            }

            // Seeded once, so a later region change never relabels stored results.
            var seeded = MetricUnits.FromRegion();
            _preferences.Set(UnitSystemKey, (int)seeded);
            return seeded;
        }

        set => _preferences.Set(UnitSystemKey, (int)value);
    }
}
