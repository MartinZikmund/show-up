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
    private static readonly TimeSpan _logoutTimeout = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _storeGate = new(1, 1);
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

    /// <inheritdoc />
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
                SetSession(new Session(saved.AccessToken, saved.RefreshToken, DateTimeOffset.FromUnixTimeSeconds(saved.ExpiresAtUnix), saved.UserId, saved.Email), notify: false);
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
        SetSession(session, notify: true);
        await PersistAsync();
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

            if (_refreshing is null)
            {
                Session started = _current;
                Task<Session?>? self = null;

                // Task.Run: a handler that completes synchronously must not run the finally before _refreshing is set.
                // The shared refresh must also not die with one caller's token, so it ignores ct.
                self = Task.Run(async () =>
                {
                    try
                    {
                        return await RefreshCoreAsync(started);
                    }
                    finally
                    {
                        lock (_gate)
                        {
                            if (ReferenceEquals(_refreshing, self))
                            {
                                _refreshing = null;
                            }
                        }
                    }
                });
                _refreshing = self;
            }

            flight = _refreshing;
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
        try
        {
            if (session is not null && options.IsConfigured)
            {
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(_logoutTimeout);
                using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "auth/v1/logout?scope=local");
                request.Headers.Authorization = new("Bearer", session.AccessToken);
                using HttpResponseMessage response = await SendAsync(request, timeout.Token);
            }
        }
        catch (Exception ex) when (ex is SupabaseException or OperationCanceledException)
        {
            // Signing out must work offline; the server session expires on its own.
        }
        finally
        {
            SetSession(null, notify: true);
            await PersistAsync();
        }
    }

    private async Task<Session?> RefreshCoreAsync(Session started)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "auth/v1/token?grant_type=refresh_token");
        request.Content = JsonContent.Create(new RefreshRequest(started.RefreshToken), SupabaseJsonContext.Default.RefreshRequest);
        using HttpResponseMessage response = await SendAsync(request, CancellationToken.None);

        Session? next = null;
        if (response.IsSuccessStatusCode)
        {
            next = await ReadSessionAsync(response, started.Email, CancellationToken.None);
        }
        else
        {
            SupabaseException error = await SupabaseErrors.FromResponseAsync(response, CancellationToken.None);
            if (!IsRefreshRejection(error))
            {
                throw error;
            }
        }

        // Signed out, or signed in as someone else, while this was in flight: the result is not ours to apply.
        if (!SwapSession(started, next))
        {
            return Current;
        }

        await PersistAsync();
        return next;
    }

    // Only the server saying the refresh token is dead signs the device out; a proxy 403 or a 408 does not.
    private static bool IsRefreshRejection(SupabaseException error) =>
        error.StatusCode is 400 or 401
        && error.ErrorCode is "refresh_token_not_found" or "refresh_token_already_used" or "session_not_found"
            or "session_expired" or "bad_jwt" or "user_not_found" or "user_banned" or "invalid_grant";

    private bool SwapSession(Session expected, Session? next)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_current, expected))
            {
                return false;
            }

            _current = next;
        }

        SessionChanged?.Invoke(this, next);
        return true;
    }

    private void SetSession(Session? session, bool notify)
    {
        lock (_gate)
        {
            _current = session;
        }

        if (notify)
        {
            SessionChanged?.Invoke(this, session);
        }
    }

    // Writes whatever is current now, so a slow save can never resurrect a session that was signed out meanwhile.
    private async Task PersistAsync()
    {
        await _storeGate.WaitAsync();
        try
        {
            Session? session = Current;
            if (session is null)
            {
                await store.ClearAsync();
                return;
            }

            StoredSession saved = new(session.AccessToken, session.RefreshToken, session.ExpiresAt.ToUnixTimeSeconds(), session.UserId, session.Email);
            await store.WriteAsync(JsonSerializer.Serialize(saved, SupabaseJsonContext.Default.StoredSession));
        }
        catch (Exception ex)
        {
            logger?.LogWarning("Saving the session failed ({ExceptionType}).", ex.GetType().Name);
        }
        finally
        {
            _storeGate.Release();
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
