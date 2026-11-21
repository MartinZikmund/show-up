using ShowUp.Core.Storage;

namespace ShowUp.Core.Tests.Storage;

internal sealed class FakeUserScope : IUserScope
{
    public string? CurrentUserId { get; set; }
}

internal sealed class FakeDeviceIdProvider : IDeviceIdProvider
{
    public string DeviceId { get; set; } = "device-1";
}
