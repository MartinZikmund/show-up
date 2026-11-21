using System.Diagnostics.CodeAnalysis;
using MZikmund.Toolkit.WinUI.Services;
using ShowUp.Core.Supabase;
using ShowUp.Core.Sync;

namespace ShowUp.Core.Tests.Accounts;

/// <summary>Code "123456" signs in; the user id is derived from the email, so the same email is the same account.</summary>
internal sealed class FakeSupabaseAuth : ISupabaseAuth
{
    public const string GoodCode = "123456";

    public Session? Current { get; private set; }

    public Session? Saved { get; set; }

    public SupabaseException? SendError { get; set; }

    public List<string> CodesSentTo { get; } = [];

    public event EventHandler<Session?>? SessionChanged;

    public static string UserIdOf(string email) => $"user-{email.ToLowerInvariant()}";

    public Task RestoreAsync(CancellationToken ct = default)
    {
        Current = Saved;
        return Task.CompletedTask;
    }

    public Task SendCodeAsync(string email, CancellationToken ct = default)
    {
        if (SendError is not null)
        {
            throw SendError;
        }

        CodesSentTo.Add(email);
        return Task.CompletedTask;
    }

    public Task<Session> VerifyCodeAsync(string email, string code, CancellationToken ct = default)
    {
        if (code != GoodCode)
        {
            throw new SupabaseException(SupabaseErrorKind.InvalidCode, "Token has expired or is invalid", 403, "otp_expired");
        }

        Session session = new("access", "refresh", DateTimeOffset.MaxValue, UserIdOf(email), email);
        Current = session;
        SessionChanged?.Invoke(this, session);
        return Task.FromResult(session);
    }

    public Task<Session?> RefreshAsync(CancellationToken ct = default) => Task.FromResult(Current);

    public Task SignOutAsync(CancellationToken ct = default)
    {
        Current = null;
        SessionChanged?.Invoke(this, null);
        return Task.CompletedTask;
    }

    /// <summary>What a refresh token the server revoked does: signed out without anyone asking.</summary>
    public void ForceSignOut()
    {
        Current = null;
        SessionChanged?.Invoke(this, null);
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken ct = default) => Task.FromResult(Current?.AccessToken);

    public Task<string?> RefreshAfterRejectionAsync(string rejectedToken, CancellationToken ct = default) => Task.FromResult(Current?.AccessToken);
}

internal sealed class FakeSyncStatus : ISyncStatus
{
    public bool HasUnsynced { get; set; }

    public Task<bool> HasUnsyncedChangesAsync() => Task.FromResult(HasUnsynced);
}

internal sealed class MemoryPreferences : IPreferences
{
    public Dictionary<string, object> Values { get; } = [];

    public T Get<T>(string key, T defaultValue) => Values.TryGetValue(key, out var v) ? (T)v : defaultValue;

    public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value)
    {
        if (Values.TryGetValue(key, out var v))
        {
            value = (T)v;
            return true;
        }

        value = default;
        return false;
    }

    public void Set<T>(string key, T? value) => Values[key] = value!;

    public T GetComplex<T>(string key, T defaultValue) => Get(key, defaultValue);

    public bool TryGetComplex<T>(string key, [MaybeNullWhen(false)] out T value) => TryGet(key, out value);

    public void SetComplex<T>(string key, T? value) => Set(key, value);

    public bool ContainsKey(string key) => Values.ContainsKey(key);

    public void Remove(string key) => Values.Remove(key);

    public void Clear() => Values.Clear();
}
