namespace ShowUp.Core.Supabase;

public enum SupabaseErrorKind
{
    NotConfigured,
    NotSignedIn,
    Network,
    InvalidCode,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    Validation,
    RateLimited,
    Server,
    InvalidResponse,
}

/// <summary>A failed Supabase call. Never carries tokens or request bodies.</summary>
public sealed class SupabaseException(
    SupabaseErrorKind kind,
    string message,
    int? statusCode = null,
    string? errorCode = null,
    TimeSpan? retryAfter = null,
    Exception? inner = null) : Exception(message, inner)
{
    public SupabaseErrorKind Kind { get; } = kind;

    public int? StatusCode { get; } = statusCode;

    /// <summary>GoTrue <c>error_code</c> or PostgREST <c>code</c>, when the server sent one.</summary>
    public string? ErrorCode { get; } = errorCode;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}
