namespace ShowUp.Core.Supabase;

public interface ISupabaseAuth : IAccessTokenProvider
{
    Session? Current { get; }

    /// <summary>
    /// Raised on sign-in, sign-out and refresh (including a forced sign-out), on whatever thread finished the call, so
    /// marshal to the UI thread before touching it. Not raised by <see cref="RestoreAsync"/>.
    /// </summary>
    event EventHandler<Session?>? SessionChanged;

    /// <summary>Loads the saved session, if any. Call once at startup; does no network.</summary>
    Task RestoreAsync(CancellationToken ct = default);

    /// <summary>Emails a 6-digit code, creating the account on first use.</summary>
    Task SendCodeAsync(string email, CancellationToken ct = default);

    Task<Session> VerifyCodeAsync(string email, string code, CancellationToken ct = default);

    /// <summary>Single-flight. A refresh token the server no longer knows signs out locally.</summary>
    Task<Session?> RefreshAsync(CancellationToken ct = default);

    /// <summary>Best-effort server logout; the local session is always cleared.</summary>
    Task SignOutAsync(CancellationToken ct = default);
}
