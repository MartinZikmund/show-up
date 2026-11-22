using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ShowUp.Core.Supabase;

/// <summary>PostgREST over <see cref="HttpClient"/>. Sends the publishable key and the user's JWT; retries a 401 once after a refresh.</summary>
public sealed class SupabaseRest(HttpClient http, SupabaseOptions options, IAccessTokenProvider tokens) : ISupabaseRest
{
    public async Task<IReadOnlyList<T>> UpsertAsync<T>(string table, IReadOnlyList<T> rows, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        byte[] body = SerializeRows(rows, typeInfo);
        return await SendAsync(
            () =>
            {
                HttpRequestMessage request = new(HttpMethod.Post, $"{options.Url}/rest/v1/{Uri.EscapeDataString(table)}?on_conflict=id");
                request.Headers.Add("Prefer", "resolution=merge-duplicates,return=representation");
                ByteArrayContent content = new(body);
                content.Headers.ContentType = new("application/json");
                request.Content = content;
                return request;
            },
            typeInfo,
            ct);
    }

    public Task<IReadOnlyList<T>> SelectPageAsync<T>(
        string table,
        DateTimeOffset? cursor,
        Guid? lastId,
        int limit,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, 0);

        StringBuilder query = new("select=*&order=server_updated_at.asc,id.asc&limit=");
        query.Append(limit);
        if (cursor is { } at)
        {
            string raw = SupabaseErrors.FormatTimestamp(at);
            string stamp = Uri.EscapeDataString(raw);
            if (lastId is { } id)
            {
                string keyset = $"(server_updated_at.gt.\"{raw}\",and(server_updated_at.eq.\"{raw}\",id.gt.\"{id}\"))";
                query.Append("&or=").Append(Uri.EscapeDataString(keyset));
            }
            else
            {
                query.Append($"&server_updated_at=gte.{stamp}");
            }
        }

        string url = $"{options.Url}/rest/v1/{Uri.EscapeDataString(table)}?{query}";
        return SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), typeInfo, ct);
    }

    public async Task<IReadOnlyList<T>> SelectAsync<T>(string table, string? filter, string order, int offset, int limit, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (!options.IsConfigured)
        {
            throw new SupabaseException(SupabaseErrorKind.NotConfigured, "Supabase is not configured.");
        }

        string query = $"select=*&order={order}&limit={limit}&offset={offset}" + (string.IsNullOrEmpty(filter) ? "" : $"&{filter}");
        using HttpRequestMessage request = new(HttpMethod.Get, $"{options.Url}/rest/v1/{Uri.EscapeDataString(table)}?{query}");
        request.Headers.Add("apikey", options.PublishableKey);
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, ct);
            return await ReadAsync(response, typeInfo, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            throw SupabaseErrors.Network(ex);
        }
    }

    private async Task<IReadOnlyList<T>> SendAsync<T>(Func<HttpRequestMessage> build, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        if (!options.IsConfigured)
        {
            throw new SupabaseException(SupabaseErrorKind.NotConfigured, "Supabase is not configured.");
        }

        string token = await tokens.GetAccessTokenAsync(ct)
            ?? throw new SupabaseException(SupabaseErrorKind.NotSignedIn, "Sign in to sync.");

        using HttpResponseMessage response = await SendOnceAsync(build, token, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            string retryToken = await tokens.RefreshAfterRejectionAsync(token, ct)
                ?? throw new SupabaseException(SupabaseErrorKind.NotSignedIn, "The session has ended. Sign in again.");
            using HttpResponseMessage retry = await SendOnceAsync(build, retryToken, ct);
            return await ReadAsync(retry, typeInfo, ct);
        }

        return await ReadAsync(response, typeInfo, ct);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(Func<HttpRequestMessage> build, string token, CancellationToken ct)
    {
        using HttpRequestMessage request = build();
        request.Headers.Add("apikey", options.PublishableKey);
        request.Headers.Authorization = new("Bearer", token);
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            throw SupabaseErrors.Network(ex);
        }
    }

    private static async Task<IReadOnlyList<T>> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await SupabaseErrors.FromResponseAsync(response, ct);
        }

        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new SupabaseException(SupabaseErrorKind.InvalidResponse, "Expected a list of rows.");
            }

            List<T> result = new(document.RootElement.GetArrayLength());
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                result.Add(element.Deserialize(typeInfo) ?? throw new SupabaseException(SupabaseErrorKind.InvalidResponse, "A row was null."));
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new SupabaseException(SupabaseErrorKind.InvalidResponse, "Supabase sent rows that could not be read.", inner: ex);
        }
    }

    private static byte[] SerializeRows<T>(IReadOnlyList<T> rows, JsonTypeInfo<T> typeInfo)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartArray();
            foreach (T row in rows)
            {
                JsonSerializer.Serialize(writer, row, typeInfo);
            }

            writer.WriteEndArray();
        }

        return stream.ToArray();
    }
}
