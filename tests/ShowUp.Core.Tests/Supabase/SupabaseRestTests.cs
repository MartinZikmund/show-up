using System.Net;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using ShowUp.Core.Supabase;

namespace ShowUp.Core.Tests.Supabase;

public sealed partial record TestRow(Guid Id, string Name, DateTimeOffset UpdatedAt, DateTimeOffset? ServerUpdatedAt, DateTimeOffset? DeletedAt);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(TestRow))]
internal sealed partial class TestRowContext : JsonSerializerContext
{
}

[TestClass]
public sealed class SupabaseRestTests
{
    private const string Url = "https://abc.supabase.co";
    private const string Key = "sb_publishable_test";
    private static readonly Guid Id1 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private readonly FakeHandler _handler = new();
    private readonly FakeTokens _tokens = new();

    private SupabaseRest Create(string url = Url, string key = Key) =>
        new(new HttpClient(_handler), new SupabaseOptions { Url = url, PublishableKey = key }, _tokens);

    [TestMethod]
    public async Task UpsertAsync_Rows_PostsMergeDuplicatesWithBothAuthHeaders()
    {
        _handler.Enqueue(HttpStatusCode.Created, "[]");

        await Create().UpsertAsync("completions", [Row("a")], TestRowContext.Default.TestRow);

        SentRequest sent = _handler.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Uri.ToString().Should().Be($"{Url}/rest/v1/completions?on_conflict=id");
        sent.Headers["apikey"].Should().Be(Key);
        sent.Headers["Authorization"].Should().Be("Bearer jwt-1");
        sent.Headers["Prefer"].Should().Be("resolution=merge-duplicates,return=representation");
        sent.Headers["Content-Type"].Should().Be("application/json");
        sent.Body.Should().StartWith("[{").And.EndWith("}]");
        sent.Body.Should().Contain("\"updated_at\":\"2026-11-21T10:00:00.123456+00:00\"");
    }

    [TestMethod]
    public async Task UpsertAsync_ServerReturnsWinningRow_ReturnsIt()
    {
        _handler.Enqueue(HttpStatusCode.Created, $$"""[{"id":"{{Id1}}","name":"server","updated_at":"2026-11-21T12:00:00.654321+00:00","server_updated_at":"2026-11-21T12:00:01.000001+00:00","deleted_at":null}]""");

        IReadOnlyList<TestRow> rows = await Create().UpsertAsync("completions", [Row("stale")], TestRowContext.Default.TestRow);

        rows.Should().ContainSingle();
        rows[0].Name.Should().Be("server");
        rows[0].UpdatedAt.Ticks.Should().Be(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero).Ticks + 6543210);
        rows[0].ServerUpdatedAt!.Value.Ticks.Should().Be(new DateTimeOffset(2026, 11, 21, 12, 0, 1, TimeSpan.Zero).Ticks + 10);
    }

    [TestMethod]
    public async Task UpsertAsync_NoRows_DoesNotCallTheServer()
    {
        IReadOnlyList<TestRow> rows = await Create().UpsertAsync("completions", [], TestRowContext.Default.TestRow);

        rows.Should().BeEmpty();
        _handler.Requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SelectPageAsync_NoCursor_OrdersAndLimitsOnly()
    {
        _handler.Enqueue(HttpStatusCode.OK, "[]");

        await Create().SelectPageAsync("notes", null, null, 500, TestRowContext.Default.TestRow);

        SentRequest sent = _handler.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Get);
        sent.Uri.ToString().Should().Be($"{Url}/rest/v1/notes?select=*&order=server_updated_at.asc,id.asc&limit=500");
        sent.Headers["apikey"].Should().Be(Key);
        sent.Headers["Authorization"].Should().Be("Bearer jwt-1");
        sent.Headers.Should().NotContainKey("Prefer");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-5)]
    public async Task SelectPageAsync_NonPositiveLimit_Throws(int limit)
    {
        Func<Task> act = () => Create().SelectPageAsync("notes", null, null, limit, TestRowContext.Default.TestRow);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        _handler.Requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SelectPageAsync_CursorOnly_UsesGteWithMicrosecondTimestamp()
    {
        _handler.Enqueue(HttpStatusCode.OK, "[]");
        DateTimeOffset cursor = new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero).AddTicks(1234567);

        await Create().SelectPageAsync("notes", cursor, null, 100, TestRowContext.Default.TestRow);

        _handler.Requests.Single().Uri.Query.Should().Contain("&server_updated_at=gte.2026-11-21T10%3A00%3A00.123456Z");
    }

    [TestMethod]
    public async Task SelectPageAsync_CursorAndLastId_ResumesStrictlyAfterThePair()
    {
        _handler.Enqueue(HttpStatusCode.OK, "[]");
        DateTimeOffset cursor = new(2026, 11, 21, 10, 0, 0, TimeSpan.FromHours(1));

        await Create().SelectPageAsync("notes", cursor, Id1, 100, TestRowContext.Default.TestRow);

        string query = Uri.UnescapeDataString(_handler.Requests.Single().Uri.Query);
        query.Should().Contain($"&or=(server_updated_at.gt.\"2026-11-21T09:00:00.000000Z\",and(server_updated_at.eq.\"2026-11-21T09:00:00.000000Z\",id.gt.\"{Id1}\"))");
    }

    [TestMethod]
    public async Task SelectPageAsync_ReadsRowsWithMicrosecondTimestamps()
    {
        _handler.Enqueue(HttpStatusCode.OK, $$"""[{"id":"{{Id1}}","name":"n","updated_at":"2026-11-21T10:00:00.123456+00:00","server_updated_at":"2026-11-21T10:00:00.999999Z","deleted_at":"2026-11-21T11:00:00+00:00"}]""");

        IReadOnlyList<TestRow> rows = await Create().SelectPageAsync("notes", null, null, 10, TestRowContext.Default.TestRow);

        rows[0].ServerUpdatedAt!.Value.Ticks.Should().Be(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero).Ticks + 9999990);
        rows[0].DeletedAt.Should().Be(new DateTimeOffset(2026, 11, 21, 11, 0, 0, TimeSpan.Zero));
    }

    [TestMethod]
    public async Task SelectPageAsync_ExpiredJwt_RefreshesOnceAndRetriesWithTheNewToken()
    {
        _handler.Enqueue(HttpStatusCode.Unauthorized, """{"code":"PGRST303","message":"JWT expired"}""");
        _handler.Enqueue(HttpStatusCode.OK, "[]");

        await Create().SelectPageAsync("notes", null, null, 10, TestRowContext.Default.TestRow);

        _handler.Requests.Should().HaveCount(2);
        _handler.Requests[0].Headers["Authorization"].Should().Be("Bearer jwt-1");
        _handler.Requests[1].Headers["Authorization"].Should().Be("Bearer jwt-2");
        _tokens.Rejected.Should().Equal("jwt-1");
    }

    [TestMethod]
    public async Task UpsertAsync_SecondUnauthorized_ThrowsWithoutLooping()
    {
        _handler.Enqueue(HttpStatusCode.Unauthorized, """{"code":"PGRST303","message":"JWT expired"}""");
        _handler.Enqueue(HttpStatusCode.Unauthorized, """{"code":"PGRST303","message":"JWT expired"}""");

        Func<Task> act = () => Create().UpsertAsync("notes", [Row("a")], TestRowContext.Default.TestRow);

        SupabaseException ex = (await act.Should().ThrowAsync<SupabaseException>()).Which;
        ex.Kind.Should().Be(SupabaseErrorKind.Unauthorized);
        ex.ErrorCode.Should().Be("PGRST303");
        _handler.Requests.Should().HaveCount(2);
        _tokens.Rejected.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task SelectPageAsync_SessionGoneOnRefresh_ThrowsNotSignedIn()
    {
        _tokens.RefreshResult = null;
        _handler.Enqueue(HttpStatusCode.Unauthorized, "{}");

        Func<Task> act = () => Create().SelectPageAsync("notes", null, null, 10, TestRowContext.Default.TestRow);

        (await act.Should().ThrowAsync<SupabaseException>()).Which.Kind.Should().Be(SupabaseErrorKind.NotSignedIn);
    }

    [TestMethod]
    public async Task SelectPageAsync_SignedOut_ThrowsNotSignedInWithoutCalling()
    {
        _tokens.Current = null;

        Func<Task> act = () => Create().SelectPageAsync("notes", null, null, 10, TestRowContext.Default.TestRow);

        (await act.Should().ThrowAsync<SupabaseException>()).Which.Kind.Should().Be(SupabaseErrorKind.NotSignedIn);
        _handler.Requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SelectPageAsync_NotConfigured_ThrowsNotConfigured()
    {
        Func<Task> act = () => Create(url: "", key: "").SelectPageAsync("notes", null, null, 10, TestRowContext.Default.TestRow);

        (await act.Should().ThrowAsync<SupabaseException>()).Which.Kind.Should().Be(SupabaseErrorKind.NotConfigured);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.Forbidden, SupabaseErrorKind.Forbidden)]
    [DataRow(HttpStatusCode.NotFound, SupabaseErrorKind.NotFound)]
    [DataRow(HttpStatusCode.Conflict, SupabaseErrorKind.Conflict)]
    [DataRow(HttpStatusCode.BadRequest, SupabaseErrorKind.Validation)]
    [DataRow(HttpStatusCode.ServiceUnavailable, SupabaseErrorKind.Server)]
    public async Task SelectPageAsync_ErrorStatus_MapsKindAndPostgrestMessage(HttpStatusCode status, SupabaseErrorKind kind)
    {
        _handler.Enqueue(status, """{"code":"42501","message":"permission denied for table notes","details":null,"hint":null}""");

        Func<Task> act = () => Create().SelectPageAsync("notes", null, null, 10, TestRowContext.Default.TestRow);

        SupabaseException ex = (await act.Should().ThrowAsync<SupabaseException>()).Which;
        ex.Kind.Should().Be(kind);
        ex.Message.Should().Be("permission denied for table notes");
        ex.ErrorCode.Should().Be("42501");
    }

    [TestMethod]
    public async Task SelectPageAsync_ConnectionFails_ThrowsNetwork()
    {
        _handler.Throw = new HttpRequestException("down");

        Func<Task> act = () => Create().SelectPageAsync("notes", null, null, 10, TestRowContext.Default.TestRow);

        (await act.Should().ThrowAsync<SupabaseException>()).Which.Kind.Should().Be(SupabaseErrorKind.Network);
    }

    [TestMethod]
    public async Task SelectPageAsync_NotAnArray_ThrowsInvalidResponse()
    {
        _handler.Enqueue(HttpStatusCode.OK, "{}");

        Func<Task> act = () => Create().SelectPageAsync("notes", null, null, 10, TestRowContext.Default.TestRow);

        (await act.Should().ThrowAsync<SupabaseException>()).Which.Kind.Should().Be(SupabaseErrorKind.InvalidResponse);
    }

    private static TestRow Row(string name) =>
        new(Id1, name, new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero).AddTicks(1234560), null, null);

    private sealed class FakeTokens : IAccessTokenProvider
    {
        public string? Current { get; set; } = "jwt-1";

        public string? RefreshResult { get; set; } = "jwt-2";

        public List<string> Rejected { get; } = [];

        public Task<string?> GetAccessTokenAsync(CancellationToken ct = default) => Task.FromResult(Current);

        public Task<string?> RefreshAfterRejectionAsync(string rejectedToken, CancellationToken ct = default)
        {
            Rejected.Add(rejectedToken);
            return Task.FromResult(RefreshResult);
        }
    }
}
