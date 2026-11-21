using MZikmund.Toolkit.WinUI.Services;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Marking;
using ShowUp.Core.Storage;
using ShowUp.Core.Tracking;

namespace ShowUp.Services.Settings;

public sealed class AppPreferences : IAppPreferences
{
    private readonly IPreferences _preferences;
    private readonly AccountProCache _proCache;
    private readonly SyncedPersonalization _synced;
    private readonly IUserScope _scope;

    public AppPreferences(IPreferences preferences, SyncedPersonalization synced, IUserScope scope)
    {
        _scope = scope;
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _synced = synced;
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

    // Synced settings: stored through SyncedPersonalization, not in these per-device preferences.
    public CrossOffStyle CrossOffStyle
    {
        get => _synced.CrossOffStyle;
        set => _synced.CrossOffStyle = value;
    }

    public MarkerColor MarkerColor
    {
        get => _synced.MarkerColor;
        set => _synced.MarkerColor = value;
    }

    public UnitSystem UnitSystem
    {
        get => _synced.UnitSystem;
        set => _synced.UnitSystem = value;
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

    // Per account too: each account's programs are its own. Signed out keeps the original key.
    private const string SelectedEnrollmentIdKey = "SelectedEnrollmentId";
    public Guid? SelectedEnrollmentId
    {
        get => Guid.TryParse(_preferences.Get(SelectedEnrollmentKey, ""), out var id) ? id : null;
        set => _preferences.Set(SelectedEnrollmentKey, value?.ToString("D") ?? "");
    }

    private string SelectedEnrollmentKey => SelectedEnrollmentIdKey + AccountProCache.KeySuffix(_scope.CurrentUserId);
}
