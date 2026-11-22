using ShowUp.Core.Storage;

namespace ShowUp.Core.Tests.Storage;

internal sealed class FakeUserScope : IUserScope
{
    public string? CurrentUserId { get; set; }

    public event EventHandler? Changed;

    /// <summary>Switches the user and tells listeners, as the account flow does.</summary>
    public void Switch(string? userId)
    {
        CurrentUserId = userId;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class FakeDeviceIdProvider : IDeviceIdProvider
{
    public string DeviceId { get; set; } = "device-1";
}
