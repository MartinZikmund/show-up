using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Supabase;

namespace ShowUp.Core.Tests.Supabase;

[TestClass]
public sealed class SupabaseAuthTests
{
    private const string Url = "https://abc.supabase.co";
    private const string Key = "sb_publishable_test";

    private readonly FakeHandler _handler = new();
    private readonly MemoryTokenStore _store = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));

    private SupabaseAuth Create(string url = Url, string key = Key) =>
        new(new HttpClient(_handler), new SupabaseOptions { Url = url, PublishableKey = key }, _store, _time);

    [TestMethod]
    public async Task SendCodeAsync_Always_PostsEmailWithCreateUserAndApiKey()
    {
        _handler.Enqueue(HttpStatusCode.OK, "{}");

        await Create().SendCodeAsync("me@example.com");

        SentRequest sent = _handler.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Uri.ToString().Should().Be($"{Url}/auth/v1/otp");
        sent.Headers["apikey"].Should().Be(Key);
        sent.Headers.Should().NotContainKey("Authorization");
        sent.Headers["Content-Type"].Should().Be("application/json");
        sent.Body.Should().Be("""{"email":"me@example.com","create_user":true}""");
    }

    [TestMethod]
    public async Task VerifyCodeAsync_ValidCode_PostsEmailTypeAndStoresSession()
    {
        _handler.Enqueue(HttpStatusCode.OK, FakeHandler.Token());
        SupabaseAuth auth = Create();
        List<Session?> changes = [];
        auth.SessionChanged += (_, s) => changes.Add(s);

        Session session = await auth.VerifyCodeAsync("me@example.com", "123456");

        _handler.Requests.Single().Uri.ToString().Should().Be($"{Url}/auth/v1/verify");
        _handler.Requests.Single().Body.Should().Be("""{"type":"email","email":"me@example.com","token":"123456"}""");
        session.Should().Be(new Session("access-1", "refresh-1", _time.GetUtcNow().AddHours(1), "11111111-1111-1111-1111-111111111111", "me@example.com"));
        auth.Current.Should().Be(session);
        changes.Should().ContainSingle().Which.Should().Be(session);
        _store.Value.Should().Contain("refresh-1");
    }

    [TestMethod]
    public async Task VerifyCodeAsync_ExpiredOrWrongCode_ThrowsInvalidCode()
    {
        _handler.Enqueue(HttpStatusCode.Forbidden, """{"code":403,"error_code":"otp_expired","msg":"Token has expired or is invalid"}""");

        Func<Task> act = () => Create().VerifyCodeAsync("me@example.com", "000000");

        SupabaseException ex = (await act.Should().ThrowAsync<SupabaseException>()).Which;
        ex.Kind.Should().Be(SupabaseErrorKind.InvalidCode);
        ex.ErrorCode.Should().Be("otp_expired");
        ex.StatusCode.Should().Be(403);
    }

    [TestMethod]
    public async Task SendCodeAsync_RateLimited_MapsRetryAfter()
    {
        _handler.Enqueue(HttpStatusCode.TooManyRequests, """{"error_code":"over_email_send_rate_limit","msg":"slow down"}""", r => r.Headers.RetryAfter = new(TimeSpan.FromSeconds(30)));

        Func<Task> act = () => Create().SendCodeAsync("me@example.com");

        SupabaseException ex = (await act.Should().ThrowAsync<SupabaseException>()).Which;
        ex.Kind.Should().Be(SupabaseErrorKind.RateLimited);
        ex.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [TestMethod]
    [DataRow(HttpStatusCode.InternalServerError, SupabaseErrorKind.Server)]
    [DataRow(HttpStatusCode.UnprocessableEntity, SupabaseErrorKind.Validation)]
    [DataRow(HttpStatusCode.Unauthorized, SupabaseErrorKind.Unauthorized)]
    public async Task SendCodeAsync_ErrorStatus_MapsKind(HttpStatusCode status, SupabaseErrorKind kind)
    {
        _handler.Enqueue(status, "not json");

        Func<Task> act = () => Create().SendCodeAsync("me@example.com");

        (await act.Should().ThrowAsync<SupabaseException>()).Which.Kind.Should().Be(kind);
    }

    [TestMethod]
    public async Task SendCodeAsync_ConnectionFails_ThrowsNetwork()
    {
        _handler.Throw = new HttpRequestException("down");

        Func<Task> act = () => Create().SendCodeAsync("me@example.com");

        (await act.Should().ThrowAsync<SupabaseException>()).Which.Kind.Should().Be(SupabaseErrorKind.Network);
    }

    [TestMethod]
    public async Task SendCodeAsync_NotConfigured_ThrowsWithoutCalling()
    {
        Func<Task> act = () => Create(url: "", key: "").SendCodeAsync("me@example.com");

        (await act.Should().ThrowAsync<SupabaseException>()).Which.Kind.Should().Be(SupabaseErrorKind.NotConfigured);
        _handler.Requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task RestoreAsync_SavedSession_LoadsItWithoutNetwork()
    {
        _handler.Enqueue(HttpStatusCode.OK, FakeHandler.Token());
        await Create().VerifyCodeAsync("me@example.com", "123456");

        SupabaseAuth second = Create();
        await second.RestoreAsync();

        second.Current!.RefreshToken.Should().Be("refresh-1");
        _handler.Requests.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task RestoreAsync_GarbageInStore_ClearsIt()
    {
        _store.Value = "{nope";
        SupabaseAuth auth = Create();

        await auth.RestoreAsync();

        auth.Current.Should().BeNull();
        _store.Value.Should().BeNull();
    }

    [TestMethod]
    public async Task GetAccessTokenAsync_FreshToken_DoesNotRefresh()
    {
        SupabaseAuth auth = await SignedInAsync();
        _time.Advance(TimeSpan.FromMinutes(58));

        string? token = await auth.GetAccessTokenAsync();

        token.Should().Be("access-1");
        _handler.Requests.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task GetAccessTokenAsync_ExpiringWithin60Seconds_RefreshesFirst()
    {
        SupabaseAuth auth = await SignedInAsync();
        _handler.Enqueue(HttpStatusCode.OK, FakeHandler.Token("access-2", "refresh-2"));
        _time.Advance(TimeSpan.FromSeconds(3541));

        string? token = await auth.GetAccessTokenAsync();

        token.Should().Be("access-2");
        SentRequest refresh = _handler.Requests[1];
        refresh.Uri.ToString().Should().Be($"{Url}/auth/v1/token?grant_type=refresh_token");
        refresh.Body.Should().Be("""{"refresh_token":"refresh-1"}""");
        refresh.Headers["apikey"].Should().Be(Key);
        _store.Value.Should().Contain("refresh-2");
    }

    [TestMethod]
    public async Task GetAccessTokenAsync_RefreshUnreachableButTokenStillValid_ReturnsOldToken()
    {
        SupabaseAuth auth = await SignedInAsync();
        _time.Advance(TimeSpan.FromSeconds(3560));
        _handler.Throw = new HttpRequestException("down");

        string? token = await auth.GetAccessTokenAsync();

        token.Should().Be("access-1");
    }

    [TestMethod]
    public async Task GetAccessTokenAsync_SignedOut_ReturnsNull() =>
        (await Create().GetAccessTokenAsync()).Should().BeNull();

    [TestMethod]
    public async Task RefreshAsync_ConcurrentCallers_ShareOneRequest()
    {
        SupabaseAuth auth = await SignedInAsync();
        _handler.Enqueue(HttpStatusCode.OK, FakeHandler.Token("access-2", "refresh-2"));
        _handler.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<Session?> first = auth.RefreshAsync();
        Task<Session?> second = auth.RefreshAsync();
        Task<Session?> third = auth.RefreshAsync();
        _handler.Gate.SetResult();
        Session?[] results = await Task.WhenAll(first, second, third);

        _handler.Requests.Should().HaveCount(2);
        results.Should().OnlyContain(s => s!.AccessToken == "access-2");
    }

    [TestMethod]
    public async Task RefreshAsync_RefreshTokenRejected_SignsOutLocally()
    {
        SupabaseAuth auth = await SignedInAsync();
        _handler.Enqueue(HttpStatusCode.BadRequest, """{"code":400,"error_code":"refresh_token_already_used","msg":"Invalid Refresh Token"}""");
        List<Session?> changes = [];
        auth.SessionChanged += (_, s) => changes.Add(s);

        Session? result = await auth.RefreshAsync();

        result.Should().BeNull();
        auth.Current.Should().BeNull();
        _store.Value.Should().BeNull();
        changes.Should().ContainSingle().Which.Should().BeNull();
    }

    [TestMethod]
    public async Task RefreshAsync_ServerDown_KeepsTheSession()
    {
        SupabaseAuth auth = await SignedInAsync();
        _handler.Enqueue(HttpStatusCode.BadGateway);

        Func<Task> act = () => auth.RefreshAsync();

        (await act.Should().ThrowAsync<SupabaseException>()).Which.Kind.Should().Be(SupabaseErrorKind.Server);
        auth.Current.Should().NotBeNull();
    }

    [TestMethod]
    public async Task RefreshAfterRejectionAsync_AlreadyRefreshed_DoesNotRefreshAgain()
    {
        SupabaseAuth auth = await SignedInAsync();
        _handler.Enqueue(HttpStatusCode.OK, FakeHandler.Token("access-2", "refresh-2"));
        await auth.RefreshAsync();

        string? token = await auth.RefreshAfterRejectionAsync("access-1");

        token.Should().Be("access-2");
        _handler.Requests.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task SignOutAsync_Online_CallsLogoutWithBearerAndClears()
    {
        SupabaseAuth auth = await SignedInAsync();
        _handler.Enqueue(HttpStatusCode.NoContent);

        await auth.SignOutAsync();

        SentRequest logout = _handler.Requests[1];
        logout.Uri.ToString().Should().Be($"{Url}/auth/v1/logout?scope=local");
        logout.Headers["Authorization"].Should().Be("Bearer access-1");
        logout.Headers["apikey"].Should().Be(Key);
        auth.Current.Should().BeNull();
        _store.Value.Should().BeNull();
    }

    [TestMethod]
    public async Task SignOutAsync_Offline_StillClears()
    {
        SupabaseAuth auth = await SignedInAsync();
        _handler.Throw = new HttpRequestException("down");

        await auth.SignOutAsync();

        auth.Current.Should().BeNull();
        _store.Value.Should().BeNull();
    }

    [TestMethod]
    public async Task VerifyCodeAsync_IncompleteSession_ThrowsInvalidResponse()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"access_token":"a"}""");

        Func<Task> act = () => Create().VerifyCodeAsync("me@example.com", "123456");

        (await act.Should().ThrowAsync<SupabaseException>()).Which.Kind.Should().Be(SupabaseErrorKind.InvalidResponse);
    }

    [TestMethod]
    public async Task RefreshAsync_TwiceInARow_SendsTwoRequests()
    {
        SupabaseAuth auth = await SignedInAsync();
        _handler.Enqueue(HttpStatusCode.OK, FakeHandler.Token("access-2", "refresh-2"));
        _handler.Enqueue(HttpStatusCode.OK, FakeHandler.Token("access-3", "refresh-3"));

        await auth.RefreshAsync();
        Session? second = await auth.RefreshAsync();

        second!.AccessToken.Should().Be("access-3");
        _handler.Requests.Should().HaveCount(3);
        _handler.Requests[2].Body.Should().Be("""{"refresh_token":"refresh-2"}""");
    }

    [TestMethod]
    public async Task RefreshAsync_OneCallerCancels_OthersStillGetTheResult()
    {
        SupabaseAuth auth = await SignedInAsync();
        _handler.Enqueue(HttpStatusCode.OK, FakeHandler.Token("access-2", "refresh-2"));
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.Gate = gate;
        using CancellationTokenSource cts = new();

        Task<Session?> cancelled = auth.RefreshAsync(cts.Token);
        Task<Session?> patient = auth.RefreshAsync();
        await _handler.WaitForRequestsAsync(2);
        await cts.CancelAsync();
        Func<Task> act = () => cancelled;
        await act.Should().ThrowAsync<OperationCanceledException>();
        gate.SetResult();

        (await patient)!.AccessToken.Should().Be("access-2");
    }

    [TestMethod]
    public async Task RefreshAsync_SignedOutWhileInFlight_StaysSignedOut()
    {
        SupabaseAuth auth = await SignedInAsync();
        _handler.Enqueue(HttpStatusCode.NoContent);
        _handler.Enqueue(HttpStatusCode.OK, FakeHandler.Token("access-2", "refresh-2"));
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.Gate = gate;

        Task<Session?> refresh = auth.RefreshAsync();
        await _handler.WaitForRequestsAsync(2);
        await auth.SignOutAsync();
        gate.SetResult();
        Session? result = await refresh;

        result.Should().BeNull();
        auth.Current.Should().BeNull();
        _store.Value.Should().BeNull();
    }

    [TestMethod]
    public async Task RefreshAsync_OtherUserSignedInWhileInFlight_KeepsTheNewSession()
    {
        SupabaseAuth auth = await SignedInAsync();
        _handler.Enqueue(HttpStatusCode.OK, FakeHandler.Token("access-b", "refresh-b", id: "22222222-2222-2222-2222-222222222222", email: "b@example.com"));
        _handler.Enqueue(HttpStatusCode.OK, FakeHandler.Token("access-2", "refresh-2"));
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.Gate = gate;

        Task<Session?> refresh = auth.RefreshAsync();
        await _handler.WaitForRequestsAsync(2);
        await auth.VerifyCodeAsync("b@example.com", "123456");
        gate.SetResult();
        await refresh;

        auth.Current!.UserId.Should().Be("22222222-2222-2222-2222-222222222222");
        auth.Current.AccessToken.Should().Be("access-b");
        _store.Value.Should().Contain("refresh-b").And.NotContain("refresh-2");
    }

    [TestMethod]
    [DataRow(HttpStatusCode.RequestTimeout, "")]
    [DataRow(HttpStatusCode.Forbidden, "<html>proxy says no</html>")]
    [DataRow(HttpStatusCode.BadRequest, """{"error_code":"validation_failed","msg":"nope"}""")]
    [DataRow(HttpStatusCode.TooManyRequests, """{"error_code":"over_request_rate_limit"}""")]
    public async Task RefreshAsync_NotARealRejection_KeepsTheSession(HttpStatusCode status, string body)
    {
        SupabaseAuth auth = await SignedInAsync();
        _handler.Enqueue(status, body);

        Func<Task> act = () => auth.RefreshAsync();

        await act.Should().ThrowAsync<SupabaseException>();
        auth.Current.Should().NotBeNull();
        _store.Value.Should().NotBeNull();
    }

    [TestMethod]
    [DataRow(HttpStatusCode.BadRequest, """{"error_code":"refresh_token_not_found"}""")]
    [DataRow(HttpStatusCode.Unauthorized, """{"error_code":"session_expired"}""")]
    [DataRow(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Invalid Refresh Token"}""")]
    [DataRow(HttpStatusCode.Forbidden, """{"error_code":"user_banned"}""")]
    public async Task RefreshAsync_RealRejection_SignsOut(HttpStatusCode status, string body)
    {
        SupabaseAuth auth = await SignedInAsync();
        _handler.Enqueue(status, body);

        (await auth.RefreshAsync()).Should().BeNull();

        auth.Current.Should().BeNull();
        _store.Value.Should().BeNull();
    }

    [TestMethod]
    public async Task SignOutAsync_CancelledToken_StillClearsLocally()
    {
        SupabaseAuth auth = await SignedInAsync();
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await auth.SignOutAsync(cts.Token);

        auth.Current.Should().BeNull();
        _store.Value.Should().BeNull();
    }

    [TestMethod]
    public async Task SignOutAsync_ServerHangsAndCallerCancels_StillClears()
    {
        SupabaseAuth auth = await SignedInAsync();
        _handler.Enqueue(HttpStatusCode.NoContent);
        _handler.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(100));

        await auth.SignOutAsync(cts.Token);

        auth.Current.Should().BeNull();
        _store.Value.Should().BeNull();
    }

    [TestMethod]
    public async Task RefreshAsync_ThrowingSubscriber_DoesNotFaultTheRefreshOrSkipOthers()
    {
        SupabaseAuth auth = await SignedInAsync();
        _handler.Enqueue(HttpStatusCode.OK, FakeHandler.Token("access-2", "refresh-2"));
        List<string> seen = [];
        auth.SessionChanged += (_, _) => throw new InvalidOperationException("boom");
        auth.SessionChanged += (_, s) => seen.Add(s!.AccessToken);

        Session? result = await auth.RefreshAsync();

        result!.AccessToken.Should().Be("access-2");
        seen.Should().Equal("access-2");
    }

    [TestMethod]
    public async Task SignOutAsync_ThrowingSubscriber_StillLeavesTheStoreCleared()
    {
        SupabaseAuth auth = await SignedInAsync();
        _handler.Enqueue(HttpStatusCode.NoContent);
        string? storeAtEvent = "unset";
        auth.SessionChanged += (_, _) =>
        {
            storeAtEvent = _store.Value;
            throw new InvalidOperationException("boom");
        };

        await auth.SignOutAsync();

        storeAtEvent.Should().BeNull();
        auth.Current.Should().BeNull();
    }

    private async Task<SupabaseAuth> SignedInAsync()
    {
        _handler.Enqueue(HttpStatusCode.OK, FakeHandler.Token());
        SupabaseAuth auth = Create();
        await auth.VerifyCodeAsync("me@example.com", "123456");
        return auth;
    }
}
