namespace ShowUp.Core.Supabase;

public sealed record Session(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, string UserId, string? Email);
