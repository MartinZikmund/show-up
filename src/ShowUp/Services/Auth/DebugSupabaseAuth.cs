#if DEBUG
using System.Security.Cryptography;
using System.Text;
using MZikmund.Toolkit.WinUI.Services;
using ShowUp.Core.Supabase;

namespace ShowUp.Services.Auth;

/// <summary>
/// Debug builds only: with "Simulate sign-in" on, sign-in talks to a fake account server instead of Supabase, so the
/// account card, the claim and account switching can be tried without a hosted project. Any email, code 123456.
/// </summary>
public sealed class DebugSupabaseAuth : ISupabaseAuth
{
    public const string Code = "123456";

    private const string SimulateKey = "DebugSimulateSignIn";
    private const string SessionKey = "DebugSimulatedSession";

    private readonly ISupabaseAuth _real;
    private readonly IPreferences _preferences;
    private Session? _simulated;

    public DebugSupabaseAuth(ISupabaseAuth real, IPreferences preferences)
    {
        _real = real;
        _preferences = preferences;
        _real.SessionChanged += (_, session) =>
        {
            if (!Simulate)
            {
                SessionChanged?.Invoke(this, session);
            }
        };
    }

    /// <summary>Switch it only while signed out; it sticks across launches.</summary>
    public bool Simulate
    {
        get => _preferences.Get(SimulateKey, false);
        set => _preferences.Set(SimulateKey, value);
    }

    public Session? Current => Simulate ? _simulated : _real.Current;

    public event EventHandler<Session?>? SessionChanged;

    public Task RestoreAsync(CancellationToken ct = default)
    {
        if (!Simulate)
        {
            return _real.RestoreAsync(ct);
        }

        _simulated = _preferences.Get(SessionKey, "") is { Length: > 0 } email ? CreateSession(email) : null;
        return Task.CompletedTask;
    }

    public async Task SendCodeAsync(string email, CancellationToken ct = default)
    {
        if (!Simulate)
        {
            await _real.SendCodeAsync(email, ct);
            return;
        }

        await Task.Delay(400, ct);
        // Prefixes that fake the server's failures.
        if (email.StartsWith("ratelimit", StringComparison.OrdinalIgnoreCase))
        {
            throw new SupabaseException(SupabaseErrorKind.RateLimited, "Simulated rate limit.", 429, "over_email_send_rate_limit");
        }

        if (email.StartsWith("offline", StringComparison.OrdinalIgnoreCase))
        {
            throw new SupabaseException(SupabaseErrorKind.Network, "Simulated network failure.");
        }
    }

    public async Task<Session> VerifyCodeAsync(string email, string code, CancellationToken ct = default)
    {
        if (!Simulate)
        {
            return await _real.VerifyCodeAsync(email, code, ct);
        }

        await Task.Delay(400, ct);
        if (code != Code)
        {
            throw new SupabaseException(SupabaseErrorKind.InvalidCode, "Simulated wrong code.", 403, "otp_expired");
        }

        _simulated = CreateSession(email);
        _preferences.Set(SessionKey, email);
        SessionChanged?.Invoke(this, _simulated);
        return _simulated;
    }

    public Task<Session?> RefreshAsync(CancellationToken ct = default) => Simulate ? Task.FromResult(_simulated) : _real.RefreshAsync(ct);

    public Task SignOutAsync(CancellationToken ct = default)
    {
        if (!Simulate)
        {
            return _real.SignOutAsync(ct);
        }

        _simulated = null;
        _preferences.Remove(SessionKey);
        SessionChanged?.Invoke(this, null);
        return Task.CompletedTask;
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken ct = default) =>
        Simulate ? Task.FromResult<string?>(null) : _real.GetAccessTokenAsync(ct);

    public Task<string?> RefreshAfterRejectionAsync(string rejectedToken, CancellationToken ct = default) =>
        Simulate ? Task.FromResult<string?>(null) : _real.RefreshAfterRejectionAsync(rejectedToken, ct);

    // The same email is always the same account, like the real server.
    private static Session CreateSession(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var userId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)).AsSpan(0, 16)).ToString();
        return new("simulated", "simulated", DateTimeOffset.MaxValue, userId, normalized);
    }
}
#endif
