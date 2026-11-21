using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ShowUp.Core.Supabase;

/// <summary>GoTrue email-code sign-in over <see cref="HttpClient"/>. Never logs or throws tokens.</summary>
public sealed class SupabaseAuth(
    HttpClient http,
    SupabaseOptions options,
    ITokenStore store,
    TimeProvider time,
    ILogger<SupabaseAuth>? logger = null) : ISupabaseAuth
{
    private static readonly TimeSpan _refreshWindow = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private Session? _current;
    private Task<Session?>? _refreshing;

    public Session? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event EventHandler<Session?>? SessionChanged;

    public async Task RestoreAsync(CancellationToken ct = default)
    {
        if (!options.IsConfigured)
        {
            return;
        }

        string? json = await store.ReadAsync(ct);
        if (json is null)
        {
            return;
        }

        try
        {
            StoredSession? saved = JsonSerializer.Deserialize(json, SupabaseJsonContext.Default.StoredSession);
            if (saved is not null)
            {
                SetSession(new Session(saved.AccessToken, saved.RefreshToken, DateTimeOffset.FromUnixTimeSeconds(saved.ExpiresAtUnix), saved.UserId, saved.Email), persist: false);
            }
        }
        catch (JsonException)
        {
            await store.ClearAsync(ct);
        }
    }

    public async Task SendCodeAsync(string email, CancellationToken ct = default)
    {
        EnsureConfigured();
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "auth/v1/otp");
        request.Content = JsonContent.Create(new OtpRequest(email, CreateUser: true), SupabaseJsonContext.Default.OtpRequest);
        using HttpResponseMessage response = await SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<Session> VerifyCodeAsync(string email, string code, CancellationToken ct = default)
    {
        EnsureConfigured();
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "auth/v1/verify");
        request.Content = JsonContent.Create(new VerifyRequest("email", email, code), SupabaseJsonContext.Default.VerifyRequest);
        using HttpResponseMessage response = await SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        Session session = await ReadSessionAsync(response, email, ct);
        SetSession(session, persist: true);
        await SaveAsync(session);
        return session;
    }

    public Task<Session?> RefreshAsync(CancellationToken ct = default)
    {
        Task<Session?> flight;
        lock (_gate)
        {
            if (_current is null)
            {
                return Task.FromResult<Session?>(null);
            }

            // The shared refresh must not die with one caller's token.
            flight = _refreshing ??= RefreshCoreAsync();
        }

        return flight.WaitAsync(ct);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        Session? session = Current;
        if (session is null)
        {
            return null;
        }

        if (session.ExpiresAt - time.GetUtcNow() > _refreshWindow)
        {
            return session.AccessToken;
        }

        try
        {
            return (await RefreshAsync(ct))?.AccessToken;
        }
        catch (SupabaseException ex) when (ex.Kind is SupabaseErrorKind.Network or SupabaseErrorKind.Server or SupabaseErrorKind.RateLimited
            && session.ExpiresAt > time.GetUtcNow())
        {
            // The server is out of reach but the token still works.
            return session.AccessToken;
        }
    }

    public async Task<string?> RefreshAfterRejectionAsync(string rejectedToken, CancellationToken ct = default)
    {
        Session? session = Current;
        if (session is null)
        {
            return null;
        }

        if (session.AccessToken != rejectedToken)
        {
            return session.AccessToken;
        }

        return (await RefreshAsync(ct))?.AccessToken;
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        Session? session = Current;
        if (session is not null && options.IsConfigured)
        {
            try
            {
                using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "auth/v1/logout?scope=local");
                request.Headers.Authorization = new("Bearer", session.AccessToken);
                using HttpResponseMessage response = await SendAsync(request, ct);
            }
            catch (SupabaseException)
            {
                // Signing out must work offline; the server session expires on its own.
            }
        }

        SetSession(null, persist: true);
        await ClearSavedAsync();
    }

    private async Task<Session?> RefreshCoreAsync()
    {
        try
        {
            Session? session = Current;
            if (session is null)
            {
                return null;
            }

            using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "auth/v1/token?grant_type=refresh_token");
            request.Content = JsonContent.Create(new RefreshRequest(session.RefreshToken), SupabaseJsonContext.Default.RefreshRequest);
            using HttpResponseMessage response = await SendAsync(request, CancellationToken.None);

            if (!response.IsSuccessStatusCode)
            {
                SupabaseException error = await SupabaseErrors.FromResponseAsync(response, CancellationToken.None);
                if (error.Kind is SupabaseErrorKind.Validation or SupabaseErrorKind.Unauthorized or SupabaseErrorKind.Forbidden)
                {
                    // The refresh token is spent or revoked: this device is signed out.
                    SetSession(null, persist: true);
                    await ClearSavedAsync();
                    return null;
                }

                throw error;
            }

            Session refreshed = await ReadSessionAsync(response, session.Email, CancellationToken.None);
            SetSession(refreshed, persist: true);
            await SaveAsync(refreshed);
            return refreshed;
        }
        finally
        {
            lock (_gate)
            {
                _refreshing = null;
            }
        }
    }

    private void SetSession(Session? session, bool persist)
    {
        lock (_gate)
        {
            _current = session;
        }

        if (persist)
        {
            SessionChanged?.Invoke(this, session);
        }
    }

    private async Task SaveAsync(Session session)
    {
        try
        {
            StoredSession saved = new(session.AccessToken, session.RefreshToken, session.ExpiresAt.ToUnixTimeSeconds(), session.UserId, session.Email);
            await store.WriteAsync(JsonSerializer.Serialize(saved, SupabaseJsonContext.Default.StoredSession));
        }
        catch (Exception ex)
        {
            logger?.LogWarning("Saving the session failed ({ExceptionType}).", ex.GetType().Name);
        }
    }

    private async Task ClearSavedAsync()
    {
        try
        {
            await store.ClearAsync();
        }
        catch (Exception ex)
        {
            logger?.LogWarning("Clearing the saved session failed ({ExceptionType}).", ex.GetType().Name);
        }
    }

    private async Task<Session> ReadSessionAsync(HttpResponseMessage response, string? fallbackEmail, CancellationToken ct)
    {
        TokenResponse? token;
        try
        {
            token = await response.Content.ReadFromJsonAsync(SupabaseJsonContext.Default.TokenResponse, ct);
        }
        catch (JsonException ex)
        {
            throw new SupabaseException(SupabaseErrorKind.InvalidResponse, "Supabase sent a session that could not be read.", inner: ex);
        }

        if (token is not { AccessToken: { Length: > 0 } access, RefreshToken: { Length: > 0 } refresh, User.Id: { Length: > 0 } userId })
        {
            throw new SupabaseException(SupabaseErrorKind.InvalidResponse, "Supabase sent an incomplete session.");
        }

        // Relative lifetime first: it does not depend on this device's clock being right.
        DateTimeOffset expires = token switch
        {
            { ExpiresIn: { } seconds } => time.GetUtcNow().AddSeconds(seconds),
            { ExpiresAt: { } at } => DateTimeOffset.FromUnixTimeSeconds(at),
            _ => time.GetUtcNow().AddHours(1),
        };

        return new Session(access, refresh, expires, userId, token.User.Email ?? fallbackEmail);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        HttpRequestMessage request = new(method, $"{options.Url}/{path}");
        request.Headers.Add("apikey", options.PublishableKey);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            throw SupabaseErrors.Network(ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await SupabaseErrors.FromResponseAsync(response, ct);
        }
    }

    private void EnsureConfigured()
    {
        if (!options.IsConfigured)
        {
            throw new SupabaseException(SupabaseErrorKind.NotConfigured, "Supabase is not configured.");
        }
    }
}
