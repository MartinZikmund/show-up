namespace ShowUp.Core.Supabase;

/// <summary>One secret string (the serialized session) in the platform's safest place. Implementations live in the heads.</summary>
public interface ITokenStore
{
    Task<string?> ReadAsync(CancellationToken ct = default);

    Task WriteAsync(string value, CancellationToken ct = default);

    Task ClearAsync(CancellationToken ct = default);
}
