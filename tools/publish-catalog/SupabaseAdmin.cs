using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PublishCatalog;

public sealed class PublishException(string message) : Exception(message);

/// <summary>PostgREST and Storage calls with the secret key. The key goes in <c>apikey</c> only (secret keys are not JWTs: no bearer token) and is never put in a message.</summary>
public sealed class SupabaseAdmin
{
    private const int PageSize = 1000;
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public SupabaseAdmin(HttpClient http, string url, string secretKey)
    {
        _http = http;
        _baseUrl = url.TrimEnd('/');
        _http.DefaultRequestHeaders.Remove("apikey");
        _http.DefaultRequestHeaders.Add("apikey", secretKey);
    }

    public async Task<ServerState> ReadStateAsync(CancellationToken ct)
    {
        var versions = await GetArrayAsync("catalog_versions?select=version&order=version.desc&limit=1", ct);
        int version = versions.Count == 0 ? 0 : versions[0].GetProperty("version").GetInt32();

        int maxRowVersion = 0;
        Dictionary<string, JsonElement> workouts = new();
        foreach (var row in await GetAllAsync("catalog_workouts?select=id,json,version&order=id.asc", ct))
        {
            workouts[row.GetProperty("id").GetString()!] = row.GetProperty("json");
            maxRowVersion = Math.Max(maxRowVersion, row.GetProperty("version").GetInt32());
        }

        var programs = (await GetAllAsync("catalog_programs?select=id,revision,json,version&order=id.asc,revision.asc", ct))
            .Select(r =>
            {
                maxRowVersion = Math.Max(maxRowVersion, r.GetProperty("version").GetInt32());
                return new ServerProgram(r.GetProperty("id").GetString()!, r.GetProperty("revision").GetInt32(), r.GetProperty("json"));
            })
            .ToList();

        return new(version, workouts, programs, maxRowVersion);
    }

    public async Task<bool> HeroExistsAsync(HeroUpload hero, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Head, hero.PublicUrl);
        using var response = await _http.SendAsync(request, ct);
        return response.StatusCode == HttpStatusCode.OK;
    }

    public async Task UploadHeroAsync(HeroUpload hero, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"{_baseUrl}/storage/v1/object/{CatalogLoader.HeroBucket}/{hero.ObjectName}");
        request.Content = new ByteArrayContent(await File.ReadAllBytesAsync(hero.FilePath, ct));
        request.Content.Headers.ContentType = new(hero.ContentType);
        request.Headers.Add("x-upsert", "false");
        request.Headers.TryAddWithoutValidation("cache-control", "max-age=31536000, immutable");
        using var _ = await SendAsync(request, $"upload hero {hero.ObjectName}", ct);
    }

    // One request is one transaction, so the version bump of all workouts lands together.
    public Task UpsertWorkoutsAsync(IEnumerable<LocalWorkout> workouts, int version, CancellationToken ct) =>
        PostRowsAsync("catalog_workouts?on_conflict=id", "resolution=merge-duplicates",
            workouts.Select(w => Row(("id", w.Id), ("json", w.Json), ("version", version))), "write workouts", ct);

    // ignore-duplicates: an existing (id, revision) is never touched.
    public Task InsertProgramsAsync(IEnumerable<LocalProgram> programs, int version, CancellationToken ct) =>
        PostRowsAsync("catalog_programs?on_conflict=id,revision", "resolution=ignore-duplicates",
            programs.Select(p => Row(("id", p.Id), ("revision", p.Revision), ("json", p.Json), ("version", version))), "write programs", ct);

    // Plain insert: a concurrent publish of the same version fails on the primary key instead of overwriting.
    public Task InsertVersionAsync(int version, CancellationToken ct) =>
        PostRowsAsync("catalog_versions", null, [Row(("version", version))], "record the version", ct);

    private static JsonObject Row(params (string Name, object Value)[] columns)
    {
        JsonObject row = new();
        foreach (var (name, value) in columns)
        {
            row[name] = value switch
            {
                string s => JsonValue.Create(s),
                int i => JsonValue.Create(i),
                JsonElement e => JsonNode.Parse(e.GetRawText()),
                _ => throw new ArgumentException($"Unsupported column type for '{name}'."),
            };
        }

        return row;
    }

    private async Task PostRowsAsync(string path, string? resolution, IEnumerable<JsonObject> rows, string what, CancellationToken ct)
    {
        JsonArray body = new();
        foreach (var row in rows)
        {
            body.Add(row);
        }

        if (body.Count == 0)
        {
            return;
        }

        using HttpRequestMessage request = new(HttpMethod.Post, $"{_baseUrl}/rest/v1/{path}");
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Prefer", resolution is null ? "return=minimal" : $"{resolution},return=minimal");
        using var _ = await SendAsync(request, what, ct);
    }

    private async Task<List<JsonElement>> GetAllAsync(string path, CancellationToken ct)
    {
        List<JsonElement> all = new();
        for (int offset = 0; ;)
        {
            var page = await GetArrayAsync($"{path}&limit={PageSize}&offset={offset}", ct);
            if (page.Count == 0)
            {
                return all;
            }

            all.AddRange(page);
            offset += page.Count;
        }
    }

    private async Task<List<JsonElement>> GetArrayAsync(string path, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"{_baseUrl}/rest/v1/{path}");
        using var response = await SendAsync(request, $"read {path.Split('?')[0]}", ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return [.. doc.RootElement.EnumerateArray().Select(e => e.Clone())];
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string what, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new PublishException($"Could not {what}: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new PublishException($"Could not {what}: HTTP {status} {(body.Length > 500 ? body[..500] : body)}");
        }

        return response;
    }
}
