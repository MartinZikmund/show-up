using MZikmund.Toolkit.WinUI.Services;
using ShowUp.Core.Storage;

namespace ShowUp.Services.Storage;

public sealed class PreferencesDeviceIdProvider(IPreferences preferences) : IDeviceIdProvider
{
    private const string DeviceIdKey = "DeviceId";

    private readonly Lazy<string> _deviceId = new(() =>
    {
        string id = preferences.Get(DeviceIdKey, "");
        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString();
            preferences.Set(DeviceIdKey, id);
        }

        return id;
    });

    public string DeviceId => _deviceId.Value;
}
