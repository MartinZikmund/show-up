using Foundation;
using Security;
using ShowUp.Core.Supabase;

namespace ShowUp.iOS.Auth;

/// <summary>
/// iOS Keychain, readable after first unlock and never copied to another device or backup. Keychain items survive an
/// uninstall, so a reinstall can find the old session (it is refreshed or rejected like any other).
/// </summary>
public sealed class KeychainTokenStore : ITokenStore
{
    private const string Service = "ShowUp.Supabase";
    private const string Account = "session";

    public Task<string?> ReadAsync(CancellationToken ct = default)
    {
        SecRecord? found = SecKeyChain.QueryAsRecord(Query(), out SecStatusCode status);
        return Task.FromResult(status == SecStatusCode.Success ? found?.ValueData is { } data ? NSString.FromData(data, NSStringEncoding.UTF8)?.ToString() : null : null);
    }

    public Task WriteAsync(string value, CancellationToken ct = default)
    {
        NSData data = NSData.FromString(value, NSStringEncoding.UTF8);
        SecStatusCode status;
        if (SecKeyChain.QueryAsRecord(Query(), out SecStatusCode existing) is not null && existing == SecStatusCode.Success)
        {
            status = SecKeyChain.Update(Query(), new SecRecord { ValueData = data });
        }
        else
        {
            SecRecord record = Query();
            record.ValueData = data;
            record.Accessible = SecAccessible.AfterFirstUnlockThisDeviceOnly;
            status = SecKeyChain.Add(record);
        }

        if (status != SecStatusCode.Success)
        {
            throw new InvalidOperationException($"Keychain write failed: {status}.");
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        SecKeyChain.Remove(Query());
        return Task.CompletedTask;
    }

    private static SecRecord Query() => new(SecKind.GenericPassword)
    {
        Service = Service,
        Account = Account,
    };
}
