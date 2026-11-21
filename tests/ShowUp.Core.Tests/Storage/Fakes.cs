using ShowUp.Core.Storage;

namespace ShowUp.Core.Tests.Storage;

internal sealed class FakeUserScope : IUserScope
{
    public string? CurrentUserId { get; set; }

#pragma warning disable CS0067 // Tests switch the user directly.
    public event EventHandler? Changed;
#pragma warning restore CS0067
}

internal sealed class FakeDeviceIdProvider : IDeviceIdProvider
{
    public string DeviceId { get; set; } = "device-1";
}
