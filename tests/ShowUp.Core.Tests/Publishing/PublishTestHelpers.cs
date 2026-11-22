using System.Net;
using System.Text;
using System.Text.Json;
using PublishCatalog;

namespace ShowUp.Core.Tests.Publishing;

internal static class Json
{
    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}

internal static class Catalogs
{
    public static LocalWorkout Workout(string id, string name = "Name") =>
        new(id, Json.Parse($$"""{"id":"{{id}}","name":"{{name}}"}"""));

    public static LocalProgram Program(string id, int revision, string name = "Name", HeroUpload? hero = null) =>
        new(id, revision, Json.Parse($$"""{"id":"{{id}}","revision":{{revision}},"name":"{{name}}"}"""), hero);

    public static LocalCatalog Local(IEnumerable<LocalWorkout> workouts, IEnumerable<LocalProgram> programs, params string[] locked) =>
        new([.. workouts], [.. programs], locked.ToHashSet(), []);

    public static ServerState Server(int version, IEnumerable<LocalWorkout> workouts, IEnumerable<LocalProgram> programs, int maxRowVersion = 0) =>
        new(version, workouts.ToDictionary(w => w.Id, w => w.Json), [.. programs.Select(p => new ServerProgram(p.Id, p.Revision, p.Json))], maxRowVersion);

    public static ServerState EmptyServer => Server(0, [], []);
}

/// <summary>Answers the reads from the given state and records every request.</summary>
internal sealed class FakeSupabaseHandler(ServerState state) : HttpMessageHandler
{
    public List<(HttpMethod Method, Uri Uri, string Body, HttpRequestMessage Request)> Requests { get; } = [];

    public bool HeroExists { get; set; }

    /// <summary>Like PostgREST max-rows: a page can hold fewer rows than the requested limit.</summary>
    public int PageCap { get; set; } = int.MaxValue;

    public IEnumerable<string> Writes => Requests
        .Where(r => r.Method == HttpMethod.Post)
        .Select(r => (r.Uri.AbsolutePath.Contains("/storage/") ? "storage:" : "") + r.Uri.AbsolutePath.Split('/')[^1]);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!, body, request));
        var path = request.RequestUri!.AbsolutePath;
        var offsetMatch = System.Text.RegularExpressions.Regex.Match(request.RequestUri.Query, "offset=([0-9]+)");
        var offset = offsetMatch.Success ? int.Parse(offsetMatch.Groups[1].Value) : 0;

        if (request.Method == HttpMethod.Head)
        {
            return new(HeroExists ? HttpStatusCode.OK : HttpStatusCode.NotFound);
        }

        if (request.Method == HttpMethod.Get)
        {
            var rows = path switch
            {
                _ when path.EndsWith("catalog_versions") => state.Version == 0 ? "[]" : $"[{{\"version\":{state.Version}}}]",
                _ when path.EndsWith("catalog_workouts") => "[" + string.Join(',', state.Workouts.Skip(offset).Take(PageCap).Select(w => $"{{\"id\":\"{w.Key}\",\"json\":{w.Value.GetRawText()},\"version\":{state.MaxRowVersion}}}")) + "]",
                _ when path.EndsWith("catalog_programs") => "[" + string.Join(',', state.Programs.Skip(offset).Take(PageCap).Select(p => $"{{\"id\":\"{p.Id}\",\"revision\":{p.Revision},\"json\":{p.Json.GetRawText()},\"version\":{state.MaxRowVersion}}}")) + "]",
                _ => "[]",
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(rows, Encoding.UTF8, "application/json") };
        }

        return new(HttpStatusCode.Created);
    }
}
