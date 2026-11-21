using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShowUp.Core.Supabase;

/// <summary>Auth DTOs live here; sync row types are registered by the sync engine and passed to the REST client as <c>JsonTypeInfo</c>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OtpRequest))]
[JsonSerializable(typeof(VerifyRequest))]
[JsonSerializable(typeof(RefreshRequest))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(AuthErrorBody))]
[JsonSerializable(typeof(StoredSession))]
internal sealed partial class SupabaseJsonContext : JsonSerializerContext
{
}

internal sealed record OtpRequest(string Email, bool CreateUser);

internal sealed record VerifyRequest(string Type, string Email, string Token);

internal sealed record RefreshRequest(string RefreshToken);

internal sealed record TokenResponse(string? AccessToken, string? RefreshToken, long? ExpiresIn, long? ExpiresAt, TokenUser? User);

internal sealed partial record TokenUser(string? Id, string? Email);

internal sealed record AuthErrorBody(string? ErrorCode, JsonElement? Code, string? Msg, string? Message, string? Error, string? ErrorDescription);

internal sealed record StoredSession(string AccessToken, string RefreshToken, long ExpiresAtUnix, string UserId, string? Email);
