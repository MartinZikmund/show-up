using System.Security.Cryptography;
using System.Text;
using MZikmund.Toolkit.WinUI.Services;

namespace ShowUp.Core.Entitlements;

/// <summary>
/// The last known Pro state, kept per account so signing in as someone else never inherits the previous account's Pro.
/// Anonymous (no account) keeps the original keys, so existing installs carry on.
/// </summary>
public sealed class AccountProCache(IPreferences preferences)
{
    private const string ProKey = "LastKnownPro";
    private const string ExpiresKey = "LastKnownProExpires";

    // Swapped whole, so a read never mixes one account's suffix with another's seeding.
    private volatile Target _target = new("", SeedFromAnonymous: false);

    /// <summary>
    /// Switches to the user's cache; null is the anonymous one. An account this device has no state for yet reads the
    /// anonymous state until the store writes its own (a Pro buyer signing in doesn't flash Free while the store answers).
    /// </summary>
    public void UseAccount(string? userId)
    {
        var suffix = KeySuffix(userId);
        _target = new(suffix, suffix.Length > 0 && !preferences.ContainsKey(ProKey + suffix));
    }

    public bool LastKnownPro
    {
        get => preferences.Get(ProKey + ReadSuffix, false);
        set
        {
            var target = Own();
            preferences.Set(ProKey + target.Suffix, value);
        }
    }

    /// <summary>Ticks of the UTC expiry; 0 means none.</summary>
    public DateTimeOffset? LastKnownProExpires
    {
        get => preferences.Get(ExpiresKey + ReadSuffix, 0L) is var ticks and > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
        set
        {
            var target = Own();
            preferences.Set(ExpiresKey + target.Suffix, value?.UtcTicks ?? 0L);
        }
    }

    private string ReadSuffix => _target is { SeedFromAnonymous: false } target ? target.Suffix : "";

    // The first write gives the account its own state; copy the seed over so a half write never mixes the two.
    private Target Own()
    {
        var target = _target;
        if (target.SeedFromAnonymous)
        {
            preferences.Set(ProKey + target.Suffix, preferences.Get(ProKey, false));
            preferences.Set(ExpiresKey + target.Suffix, preferences.Get(ExpiresKey, 0L));
            target = target with { SeedFromAnonymous = false };
            _target = target;
        }

        return target;
    }

    /// <summary>A hash, so the raw user id never lands in the preference keys.</summary>
    public static string KeySuffix(string? userId) =>
        string.IsNullOrEmpty(userId) ? "" : "." + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(userId)).AsSpan(0, 8));

    private sealed record Target(string Suffix, bool SeedFromAnonymous);
}
