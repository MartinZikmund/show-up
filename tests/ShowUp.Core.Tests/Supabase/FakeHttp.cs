using System.Net;
using System.Text;

namespace ShowUp.Core.Tests.Supabase;

internal sealed record SentRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string? Body);

/// <summary>Replays queued responses and records what was sent.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<SentRequest> Requests { get; } = [];

    /// <summary>Holds the next request until completed.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public async Task WaitForRequestsAsync(int count)
    {
        for (int i = 0; i < 200 && Requests.Count < count; i++)
        {
            await Task.Delay(10);
        }
    }

    public Exception? Throw { get; set; }

    public FakeHandler Enqueue(HttpStatusCode status, string json = "", Action<HttpResponseMessage>? configure = null)
    {
        _responses.Enqueue(() =>
        {
            HttpResponseMessage response = new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            configure?.Invoke(response);
            return response;
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dictionary<string, string> headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        if (request.Content?.Headers.ContentType is { } type)
        {
            headers["Content-Type"] = type.MediaType ?? "";
        }

        lock (Requests)
        {
            Requests.Add(new SentRequest(request.Method, request.RequestUri!, headers, body));
        }

        if (Throw is not null)
        {
            throw Throw;
        }

        TaskCompletionSource? gate = Gate;
        Gate = null;
        if (gate is not null)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        return _responses.Dequeue()();
    }

    public static string Token(string access = "access-1", string refresh = "refresh-1", int expiresIn = 3600, string id = "11111111-1111-1111-1111-111111111111", string email = "me@example.com") =>
        $$$"""{"access_token":"{{{access}}}","token_type":"bearer","expires_in":{{{expiresIn}}},"refresh_token":"{{{refresh}}}","user":{"id":"{{{id}}}","email":"{{{email}}}"}}""";
}

internal sealed class MemoryTokenStore : ShowUp.Core.Supabase.ITokenStore
{
    public string? Value { get; set; }

    public Task<string?> ReadAsync(CancellationToken ct = default) => Task.FromResult(Value);

    public Task WriteAsync(string value, CancellationToken ct = default)
    {
        Value = value;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        Value = null;
        return Task.CompletedTask;
    }
}
