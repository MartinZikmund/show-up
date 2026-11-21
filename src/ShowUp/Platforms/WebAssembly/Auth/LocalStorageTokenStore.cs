using System.Runtime.InteropServices.JavaScript;
using ShowUp.Core.Supabase;

namespace ShowUp.Platforms.WebAssembly.Auth;

/// <summary>
/// The browser has no secure store: this is <c>localStorage</c>, readable by any script on the origin, so an XSS bug
/// would leak the session. Never loading third-party scripts is the defence.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("browser")]
public sealed partial class LocalStorageTokenStore : ITokenStore
{
    private const string Key = "showup.supabase.session";

    public Task<string?> ReadAsync(CancellationToken ct = default) => Task.FromResult(Js.Get(Key));

    public Task WriteAsync(string value, CancellationToken ct = default)
    {
        Js.Set(Key, value);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        Js.Remove(Key);
        return Task.CompletedTask;
    }

    private static partial class Js
    {
        [JSImport("globalThis.ShowUpTokens.get")]
        public static partial string? Get(string key);

        [JSImport("globalThis.ShowUpTokens.set")]
        public static partial void Set(string key, string value);

        [JSImport("globalThis.ShowUpTokens.remove")]
        public static partial void Remove(string key);
    }
}
