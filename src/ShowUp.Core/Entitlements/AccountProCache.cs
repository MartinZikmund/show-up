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

    private volatile string _suffix = "";

    /// <summary>Switches to the user's cache; null is the anonymous one.</summary>
    public void UseAccount(string? userId) => _suffix = KeySuffix(userId);

    public bool LastKnownPro
    {
        get => preferences.Get(ProKey + _suffix, false);
        set => preferences.Set(ProKey + _suffix, value);
    }

    /// <summary>Ticks of the UTC expiry; 0 means none.</summary>
    public DateTimeOffset? LastKnownProExpires
    {
        get => preferences.Get(ExpiresKey + _suffix, 0L) is var ticks and > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
        set => preferences.Set(ExpiresKey + _suffix, value?.UtcTicks ?? 0L);
    }

    /// <summary>A hash, so the raw user id never lands in the preference keys.</summary>
    public static string KeySuffix(string? userId) =>
        string.IsNullOrEmpty(userId) ? "" : "." + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(userId)).AsSpan(0, 8));
}
