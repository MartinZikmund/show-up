using System.Globalization;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using MZikmund.Toolkit.WinUI.Services;
using ShowUp.Core.Marking;
using ShowUp.Core.Messages;
using ShowUp.Core.Tracking;

namespace ShowUp.Core.Storage;

/// <summary>
/// The settings that follow the user across devices (marker color, cross-off style, units), kept in
/// <see cref="ISyncedSettings"/> behind plain synchronous properties. Reads come from memory; writes persist in order in the background.
/// The first <see cref="InitializeAsync"/> moves values an older build kept in per-device preferences.
/// </summary>
public sealed class SyncedPersonalization
{
    public const string MarkerColorKey = "MarkerColor";
    public const string CrossOffStyleKey = "CrossOffStyle";
    public const string UnitSystemKey = "UnitSystem";

    private readonly ISyncedSettings _settings;
    private readonly IPreferences _legacy;
    private readonly IMessenger _messenger;
    private readonly ILogger<SyncedPersonalization> _logger;
    private readonly object _gate = new();
    private int _writes;
    private Task _pending = Task.CompletedTask;

    public SyncedPersonalization(ISyncedSettings settings, IPreferences legacy, IMessenger messenger, ILogger<SyncedPersonalization> logger)
    {
        _settings = settings;
        _legacy = legacy;
        _messenger = messenger;
        _logger = logger;

        // Until the first load the old per-device values keep the app looking the way it did.
        _markerColor = ParseEnum(LegacyRaw(MarkerColorKey), MarkerColor.Auto);
        _crossOffStyle = ParseEnum(LegacyRaw(CrossOffStyleKey), CrossOffStyle.DrawYourself);
        _unitSystem = ParseEnum(LegacyRaw(UnitSystemKey), MetricUnits.FromRegion());
        messenger.Register<SyncedPersonalization, DataChangedMessage>(this, static (r, _) => r.ReloadInBackground());
    }

    private MarkerColor _markerColor;
    private CrossOffStyle _crossOffStyle;
    private UnitSystem _unitSystem;

    public MarkerColor MarkerColor
    {
        get => _markerColor;
        set
        {
            _markerColor = value;
            Persist(MarkerColorKey, value);
        }
    }

    public CrossOffStyle CrossOffStyle
    {
        get => _crossOffStyle;
        set
        {
            _crossOffStyle = value;
            Persist(CrossOffStyleKey, value);
        }
    }

    /// <summary>Seeded from the region the first time, so a later region change never relabels stored results.</summary>
    public UnitSystem UnitSystem
    {
        get => _unitSystem;
        set
        {
            _unitSystem = value;
            Persist(UnitSystemKey, value);
        }
    }

    /// <summary>Loads the synced values; a key with none yet takes the old per-device value (which is then removed) or the default.</summary>
    public async Task InitializeAsync()
    {
        _markerColor = await LoadAsync(MarkerColorKey, MarkerColor.Auto);
        _crossOffStyle = await LoadAsync(CrossOffStyleKey, CrossOffStyle.DrawYourself);
        _unitSystem = await LoadAsync(UnitSystemKey, MetricUnits.FromRegion());
    }

    /// <summary>Completes when every write so far has reached the store.</summary>
    public Task FlushAsync()
    {
        lock (_gate)
        {
            return _pending;
        }
    }

    private async Task<T> LoadAsync<T>(string key, T fallback)
        where T : struct, Enum
    {
        if (await _settings.GetAsync(key) is { } stored)
        {
            return ParseEnum(stored, fallback);
        }

        var legacy = LegacyRaw(key);
        var value = ParseEnum(legacy, fallback);
        await _settings.SetAsync(key, Raw(value));
        if (legacy is not null)
        {
            _legacy.Remove(key);
        }

        return value;
    }

    private string? LegacyRaw(string key) =>
        _legacy.TryGet<int>(key, out var raw) ? raw.ToString(CultureInfo.InvariantCulture) : null;

    private static string Raw<T>(T value)
        where T : struct, Enum => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    private static T ParseEnum<T>(string? raw, T fallback)
        where T : struct, Enum =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && Enum.IsDefined(typeof(T), number)
            ? (T)Enum.ToObject(typeof(T), number)
            : fallback;

    private void Persist<T>(string key, T value)
        where T : struct, Enum
    {
        var raw = Raw(value);
        Interlocked.Increment(ref _writes);
        lock (_gate)
        {
            _pending = WriteAfterAsync(_pending, key, raw);
        }
    }

    private async Task WriteAfterAsync(Task previous, string key, string raw)
    {
        await previous;
        try
        {
            await _settings.SetAsync(key, raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Saving the {Key} setting failed.", key);
        }
    }

    /// <summary>
    /// After a sign-in, claim or sync pull. A key the store has no value for keeps what is in memory (and is not written:
    /// a fresh row here would beat the account's own value on the next sync). A new marker color repaints the brushes.
    /// </summary>
    private async Task ReloadAsync()
    {
        while (true)
        {
            var writes = Volatile.Read(ref _writes);
            await FlushAsync();
            var markerColor = ParseEnum(await _settings.GetAsync(MarkerColorKey), _markerColor);
            var crossOffStyle = ParseEnum(await _settings.GetAsync(CrossOffStyleKey), _crossOffStyle);
            var unitSystem = ParseEnum(await _settings.GetAsync(UnitSystemKey), _unitSystem);

            // A pick made while this read would otherwise be undone by the older stored value.
            if (writes != Volatile.Read(ref _writes))
            {
                continue;
            }

            var colorChanged = markerColor != _markerColor;
            _markerColor = markerColor;
            _crossOffStyle = crossOffStyle;
            _unitSystem = unitSystem;
            if (colorChanged)
            {
                _messenger.Send(new MarkerColorChangedMessage(markerColor));
            }

            _messenger.Send(new PersonalizationReloadedMessage());
            return;
        }
    }

    private async void ReloadInBackground()
    {
        try
        {
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reloading the synced settings failed.");
        }
    }
}
