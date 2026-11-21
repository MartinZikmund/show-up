using Foundation;
using Security;
using ShowUp.Core.Supabase;

namespace ShowUp.iOS.Auth;

/// <summary>iOS Keychain, readable after first unlock and never copied to another device or backup.</summary>
public sealed class KeychainTokenStore : ITokenStore
{
    private const string Service = "ShowUp.Supabase";
    private const string Account = "session";

    public Task<string?> ReadAsync(CancellationToken ct = default)
    {
        SecRecord? found = SecKeyChain.QueryAsRecord(Query(), out SecStatusCode status);
        return Task.FromResult(status == SecStatusCode.Success ? found?.ValueData?.ToString() : null);
    }

    public Task WriteAsync(string value, CancellationToken ct = default)
    {
        SecKeyChain.Remove(Query());
        SecRecord record = Query();
        record.ValueData = NSData.FromString(value);
        record.Accessible = SecAccessible.AfterFirstUnlockThisDeviceOnly;
        SecStatusCode status = SecKeyChain.Add(record);
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
