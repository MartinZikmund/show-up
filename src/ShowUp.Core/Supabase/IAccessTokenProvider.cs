namespace ShowUp.Core.Supabase;

public interface IAccessTokenProvider
{
    /// <summary>A token good for at least the next minute (refreshing if needed), or null when signed out.</summary>
    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// The server rejected <paramref name="rejectedToken"/>. Refreshes unless another caller already has, and returns
    /// the token to retry with, or null if the session is gone.
    /// </summary>
    Task<string?> RefreshAfterRejectionAsync(string rejectedToken, CancellationToken ct = default);
}
