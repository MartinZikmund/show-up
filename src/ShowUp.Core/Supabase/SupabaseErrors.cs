using System.Net;
using System.Text.Json;

namespace ShowUp.Core.Supabase;

internal static class SupabaseErrors
{
    public static async Task<SupabaseException> FromResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? code = null;
        string? message = null;
        try
        {
            string body = await response.Content.ReadAsStringAsync(ct);
            if (body.Length > 0)
            {
                // GoTrue sends error_code/msg (or error/error_description); PostgREST sends code/message.
                AuthErrorBody? error = JsonSerializer.Deserialize(body, SupabaseJsonContext.Default.AuthErrorBody);
                // GoTrue's "code" is the HTTP status as a number; PostgREST's is a string.
                code = error?.ErrorCode ?? (error?.Code is { ValueKind: JsonValueKind.String } text ? text.GetString() : null);
                message = error?.Msg ?? error?.Message ?? error?.ErrorDescription ?? error?.Error;
            }
        }
        catch (JsonException)
        {
        }

        int status = (int)response.StatusCode;
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
        SupabaseErrorKind kind = Classify(response.StatusCode, code);
        return new SupabaseException(kind, message ?? $"Supabase returned {status}.", status, code, retryAfter);
    }

    public static SupabaseErrorKind Classify(HttpStatusCode status, string? code) => code switch
    {
        "otp_expired" => SupabaseErrorKind.InvalidCode,
        "PGRST301" or "PGRST303" => SupabaseErrorKind.Unauthorized,
        _ => status switch
        {
            HttpStatusCode.Unauthorized => SupabaseErrorKind.Unauthorized,
            HttpStatusCode.Forbidden => SupabaseErrorKind.Forbidden,
            HttpStatusCode.NotFound => SupabaseErrorKind.NotFound,
            HttpStatusCode.Conflict => SupabaseErrorKind.Conflict,
            HttpStatusCode.TooManyRequests => SupabaseErrorKind.RateLimited,
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => SupabaseErrorKind.Validation,
            >= HttpStatusCode.InternalServerError => SupabaseErrorKind.Server,
            _ => SupabaseErrorKind.Validation,
        },
    };

    public static SupabaseException Network(Exception inner) =>
        new(SupabaseErrorKind.Network, "Could not reach Supabase.", inner: inner);

    public static string FormatTimestamp(DateTimeOffset value)
    {
        // Postgres keeps microseconds: send exactly what it can store.
        DateTime utc = value.UtcDateTime;
        utc = utc.AddTicks(-(utc.Ticks % 10));
        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);
    }
}
