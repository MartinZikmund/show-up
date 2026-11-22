using System.Net;
using AwesomeAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using ShowUp.Core.Catalog;
using ShowUp.Core.Messages;
using ShowUp.Core.Storage;
using ShowUp.Core.Supabase;
using ShowUp.Core.Tests.CustomPrograms;
using ShowUp.Core.Tests.Storage;
using ShowUp.Core.Tests.Supabase;
using ShowUp.Core.Tests.Tracking;

namespace ShowUp.Core.Tests.Catalog;

[TestClass]
public sealed class OtaCatalogTests : StorageTestBase
{
    private const string NewWorkout = """{ "id": "yoga", "name": "Yoga", "durationMinutes": 45, "kind": "mixed", "family": null }""";

    private readonly ListLogger _log = new();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly FakeRemote _remote = new();
    private readonly MemoryCache _memoryCache = new();

    [TestMethod]
    public async Task Remote_NewerVersion_Replaces()
    {
        var (service, updates) = Create(_memoryCache);
        int changed = 0;
        _messenger.Register<DataChangedMessage>(this, (_, _) => changed++);
        (await service.GetProgramAsync("sample"))!.Revision.Should().Be(1);
        _remote.Publish(2, [NewWorkout, .. Workouts()], [Program("sample", 2, name: "Sample v2")]);

        await updates.StartAsync();

        var program = await service.GetProgramAsync("sample");
        program!.Revision.Should().Be(2);
        program.Name.Should().Be("Sample v2");
        (await service.GetWorkoutAsync("yoga"))!.Name.Should().Be("Yoga");
        (await service.GetCatalogAsync()).Version.Should().Be(2);
        changed.Should().Be(1);
    }

    [TestMethod]
    public async Task Remote_SameOrOlderVersion_NoDownload()
    {
        _memoryCache.Saved = new(3, null, []);
        var (_, updates) = Create(_memoryCache);

        _remote.Publish(3, [], []);
        await updates.StartAsync();
        _remote.Publish(2, [], []);
        await updates.ResumeAsync();

        _remote.Downloads.Should().Be(0);
    }

    [TestMethod]
    public async Task Remote_Offline_UsesCached()
    {
        _memoryCache.Saved = new(2, null, [new("sample", 2, 2, Program("sample", 2, name: "Cached"))]);
        var (service, updates) = Create(_memoryCache);
        _remote.Fail = new HttpRequestException("offline");
        int changed = 0;
        _messenger.Register<DataChangedMessage>(this, (_, _) => changed++);

        Func<Task> act = updates.StartAsync;

        await act.Should().NotThrowAsync();
        (await service.GetProgramAsync("sample"))!.Name.Should().Be("Cached");
        changed.Should().Be(0);
        _log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [TestMethod]
    public async Task PinnedRevision_StillResolvable()
    {
        var (service, updates) = Create(_memoryCache);
        _remote.Publish(2, [NewWorkout, .. Workouts()], [Program("sample", 2, name: "Sample v2")]);

        await updates.StartAsync();

        (await service.GetProgramAsync("sample"))!.Revision.Should().Be(2);
        (await service.GetProgramAsync("sample", 1))!.Name.Should().Be("Sample");
        (await service.GetProgramAsync("sample", 2))!.Name.Should().Be("Sample v2");
        (await service.GetProgramAsync("sample", 9)).Should().BeNull();
        (await service.GetCatalogAsync()).Programs.Should().ContainSingle(p => p.Id == "sample");
    }

    [TestMethod]
    public async Task Remote_InvalidProgram_IsSkippedAndBundledKept()
    {
        var (service, updates) = Create(_memoryCache);
        var broken = Program("sample", 2, name: "Broken").Replace("\"fit-test\"", "\"nope\"");
        _remote.Publish(2, [NewWorkout], [broken, Program("other", 1, name: "Other"), "{ not json"]);

        await updates.StartAsync();

        (await service.GetProgramAsync("sample"))!.Revision.Should().Be(1);
        (await service.GetProgramAsync("other"))!.Name.Should().Be("Other");
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Text.Contains("sample"));
        _memoryCache.Saved!.Programs.Select(p => p.Id).Should().Equal("other");
    }

    [TestMethod]
    public async Task Remote_WorkoutIdRemoved_RejectsWorkouts()
    {
        var (service, updates) = Create(_memoryCache);
        _remote.Publish(2, [NewWorkout, .. Workouts().Where(w => !w.Contains("\"abs\""))], [Program("sample", 2, name: "Sample v2")]);

        await updates.StartAsync();

        (await service.GetWorkoutAsync("abs")).Should().NotBeNull();
        (await service.GetWorkoutAsync("yoga")).Should().BeNull();
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Text.Contains("abs"));
        _memoryCache.Saved!.WorkoutsJson.Should().BeNull();
        (await service.GetProgramAsync("sample"))!.Revision.Should().Be(2, "a valid program still lands");
        (await service.GetCatalogAsync()).Version.Should().Be(1, "the version is not recorded");
    }

    [TestMethod]
    public async Task Remote_WorkoutsRejected_ProgramUsingNewWorkoutIsSkipped()
    {
        var (service, updates) = Create(_memoryCache);
        var usesYoga = Program("other", 1, name: "Other").Replace("\"fit-test\"", "\"yoga\"");
        _remote.Publish(2, [NewWorkout, .. Workouts().Where(w => !w.Contains("\"abs\""))], [usesYoga, Program("sample", 2, name: "Sample v2")]);

        await updates.StartAsync();

        (await service.GetProgramAsync("other")).Should().BeNull();
        (await service.GetProgramAsync("sample"))!.Revision.Should().Be(2);
    }

    [TestMethod]
    public async Task Remote_VersionMovesDuringDownload_AppliesNothingAndRetries()
    {
        var (service, updates) = Create(_memoryCache);
        _remote.Publish(2, [NewWorkout, .. Workouts()], [Program("sample", 2, name: "Sample v2")]);
        _remote.OnDownload = () => _remote.Publish(3, [NewWorkout, .. Workouts()], [Program("sample", 3, name: "Sample v3")]);

        await updates.StartAsync();

        (await service.GetProgramAsync("sample"))!.Revision.Should().Be(1);
        _memoryCache.Saved.Should().BeNull();
        _remote.OnDownload = null;
        await updates.ResumeAsync();
        (await service.GetProgramAsync("sample"))!.Revision.Should().Be(3);
    }

    [TestMethod]
    public async Task Apply_LowerOrEqualVersion_ChangesNothing()
    {
        var (service, _) = Create(_memoryCache);

        (await service.ApplyAsync(new RemoteCatalog(1, [], [new("sample", 2, Program("sample", 2, "x"))]))).Should().BeFalse();

        _memoryCache.Saved.Should().BeNull();
        (await service.GetProgramAsync("sample"))!.Revision.Should().Be(1);
    }

    [TestMethod]
    public async Task Refresh_TwoAtOnce_RunsOneCheck()
    {
        var (_, updates) = Create(_memoryCache);
        TaskCompletionSource gate = new();
        _remote.Gate = gate;

        var first = updates.StartAsync();
        var second = updates.StartAsync();
        await second;
        gate.SetResult();
        await first;

        _remote.Checks.Should().Be(1);
    }

    [TestMethod]
    [DataRow("https://evil.example/hero.png")]
    [DataRow("http://abc.supabase.co/storage/hero.png")]
    [DataRow("https://abc.supabase.co.evil.example/hero.png")]
    [DataRow("file:///C:/hero.png")]
    public async Task Hero_NotAnHttpsSupabaseUrl_IsDroppedButTheProgramStays(string hero)
    {
        var (service, updates) = Create(_memoryCache, options: Supabase());
        _remote.Publish(2, [NewWorkout, .. Workouts()], [Program("sample", 2, name: "Sample v2", hero: hero)]);

        await updates.StartAsync();

        var program = await service.GetProgramAsync("sample");
        program!.Revision.Should().Be(2);
        program.HeroImage.Should().Be("Assets/Heroes/sample.png", "the bundled art is the fallback");
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Text.Contains("hero image"));
    }

    [TestMethod]
    public async Task Hero_SupabaseStorageUrl_IsKept()
    {
        var (service, updates) = Create(_memoryCache, options: Supabase());
        const string url = "https://abc.supabase.co/storage/v1/object/public/heroes/sample.png";
        _remote.Publish(2, [NewWorkout, .. Workouts()], [Program("sample", 2, name: "Sample v2", hero: url)]);

        await updates.StartAsync();

        (await service.GetProgramAsync("sample"))!.HeroImage.Should().Be(url);
    }

    [TestMethod]
    public async Task Hero_NoSupabaseConfigured_RemoteUrlIsDroppedOnLoad()
    {
        _memoryCache.Saved = new(2, null, [new("sample", 2, 2, Program("sample", 2, name: "Cached", hero: "https://abc.supabase.co/h.png"))]);
        var (service, _) = Create(_memoryCache);

        (await service.GetProgramAsync("sample"))!.HeroImage.Should().Be("Assets/Heroes/sample.png");
    }

    [TestMethod]
    [DataRow(null, null, true)]
    [DataRow("Assets/Heroes/a.png", null, true)]
    [DataRow("https://abc.supabase.co/a.png", "abc.supabase.co", true)]
    [DataRow("https://ABC.supabase.co/a.png", "abc.supabase.co", true)]
    [DataRow("https://abc.supabase.co/a.png", null, false)]
    [DataRow("http://abc.supabase.co/a.png", "abc.supabase.co", false)]
    [DataRow("//abc.supabase.co/a.png", "abc.supabase.co", false)]
    [DataRow("https://other.example/a.png", "abc.supabase.co", false)]
    public void HeroImagePolicy_IsAllowed_OnlyBundledPathsAndHttpsOnTheHost(string? hero, string? host, bool expected) =>
        HeroImagePolicy.IsAllowed(hero, host).Should().Be(expected);

    [TestMethod]
    public async Task Remote_NewProgramUsingNewWorkout_IsAccepted()
    {
        var (service, updates) = Create(_memoryCache);
        _remote.Publish(2, [NewWorkout, .. Workouts()], [Program("other", 1, name: "Other").Replace("\"fit-test\"", "\"yoga\"")]);

        await updates.StartAsync();

        (await service.GetProgramAsync("other")).Should().NotBeNull();
    }

    [TestMethod]
    public async Task Remote_CacheWriteFails_KeepsThePreviousCatalog()
    {
        var (service, updates) = Create(_memoryCache);
        (await service.GetCatalogAsync()).Version.Should().Be(1);
        _memoryCache.SaveFails = true;
        _remote.Publish(2, [NewWorkout, .. Workouts()], [Program("sample", 2, name: "Sample v2")]);

        await updates.StartAsync();

        (await service.GetProgramAsync("sample"))!.Revision.Should().Be(1);
        (await service.GetWorkoutAsync("yoga")).Should().BeNull();
        (await service.GetCatalogAsync()).Version.Should().Be(1);
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Text.Contains("Saving catalog version"));
    }

    [TestMethod]
    public async Task Remote_DownloadFailsMidway_KeepsThePreviousCatalog()
    {
        var (service, updates) = Create(_memoryCache);
        _remote.Publish(2, [NewWorkout, .. Workouts()], [Program("sample", 2, name: "Sample v2")]);
        _remote.FailDownload = new HttpRequestException("dropped");

        await updates.StartAsync();

        (await service.GetProgramAsync("sample"))!.Revision.Should().Be(1);
        _memoryCache.Saved.Should().BeNull();
    }

    [TestMethod]
    public async Task Cache_SurvivesRestart()
    {
        SqliteCatalogCache cache = new(Db);
        var (_, updates) = Create(cache);
        _remote.Publish(2, [NewWorkout, .. Workouts()], [Program("sample", 2, name: "Sample v2")]);
        await updates.StartAsync();

        var (restarted, _) = Create(new SqliteCatalogCache(Db));

        (await restarted.GetProgramAsync("sample"))!.Name.Should().Be("Sample v2");
        (await restarted.GetProgramAsync("sample", 1))!.Name.Should().Be("Sample");
        (await restarted.GetWorkoutAsync("yoga")).Should().NotBeNull();
        (await restarted.GetInstalledVersionAsync()).Should().Be(2);
    }

    [TestMethod]
    public async Task Cache_KeepsEveryRevisionEverCached()
    {
        SqliteCatalogCache cache = new(Db);

        await cache.SaveAsync(2, "[]", [new("p", 2, 2, "{}")]);
        await cache.SaveAsync(3, "[]", [new("p", 3, 3, "{}")]);

        var loaded = await cache.LoadAsync();
        loaded!.Version.Should().Be(3);
        loaded.Programs.Select(p => p.Revision).Should().Equal(2, 3);
    }

    [TestMethod]
    public async Task Cache_NothingSaved_LoadsNull()
    {
        (await new SqliteCatalogCache(Db).LoadAsync()).Should().BeNull();
    }

    [TestMethod]
    public async Task CachedProgramInvalidNow_IsIgnoredOnLoad()
    {
        _memoryCache.Saved = new(2, null, [new("bad", 1, 2, "{ nope")]);
        var (service, _) = Create(_memoryCache);

        (await service.GetCatalogAsync()).Programs.Select(p => p.Id).Should().Equal("sample");
    }

    [TestMethod]
    public async Task NotConfigured_NoRequestsAreSent()
    {
        FakeHandler handler = new();
        SupabaseCatalogRemote remote = new(new SupabaseRest(new HttpClient(handler), new SupabaseOptions(), new NoTokens()), new SupabaseOptions());
        var (_, updates) = Create(_memoryCache, remote);

        await updates.StartAsync();
        await updates.ResumeAsync();

        remote.IsAvailable.Should().BeFalse();
        handler.Requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Resume_ChecksAtMostEveryTwelveHours()
    {
        FakeTimeProvider time = new(new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero));
        var (_, updates) = Create(_memoryCache, time: time);

        await updates.StartAsync();
        await updates.ResumeAsync();
        time.Advance(TimeSpan.FromHours(11));
        await updates.ResumeAsync();
        _remote.Checks.Should().Be(1);
        time.Advance(TimeSpan.FromHours(2));
        await updates.ResumeAsync();

        _remote.Checks.Should().Be(2);
    }

    [TestMethod]
    public async Task Resume_AfterAFailedCheck_TriesAgain()
    {
        var (_, updates) = Create(_memoryCache);
        _remote.Fail = new HttpRequestException("offline");
        await updates.StartAsync();
        _remote.Fail = null;

        await updates.ResumeAsync();

        _remote.Checks.Should().Be(2);
    }

    [TestMethod]
    public async Task SupabaseRemote_ReadsAnonymouslyAndPages()
    {
        FakeHandler handler = new();
        SupabaseOptions options = new() { Url = "https://abc.supabase.co", PublishableKey = "sb_publishable_x" };
        SupabaseCatalogRemote remote = new(new SupabaseRest(new HttpClient(handler), options, new NoTokens()), options);
        string WorkoutRows(int count, int start = 0) =>
            "[" + string.Join(',', Enumerable.Range(start, count).Select(i => $$"""{"id":"w{{i}}","json":{"id":"w{{i}}"},"version":2}""")) + "]";
        handler.Enqueue(HttpStatusCode.OK, """[{"version":2,"published_at":"2026-12-01T00:00:00Z"}]""");
        handler.Enqueue(HttpStatusCode.OK, WorkoutRows(SupabaseCatalogRemote.PageSize));
        handler.Enqueue(HttpStatusCode.OK, WorkoutRows(1, SupabaseCatalogRemote.PageSize));
        handler.Enqueue(HttpStatusCode.OK, """[{"id":"sample","revision":2,"json":{"id":"sample"},"version":2}]""");

        (await remote.GetLatestVersionAsync()).Should().Be(2);
        var download = await remote.DownloadAsync(2);

        download.Workouts.Should().HaveCount(SupabaseCatalogRemote.PageSize + 1);
        download.Programs.Should().ContainSingle().Which.Should().Be(new RemoteProgram("sample", 2, """{"id":"sample"}"""));
        handler.Requests.Should().OnlyContain(r => r.Headers["apikey"] == "sb_publishable_x" && !r.Headers.ContainsKey("Authorization"));
        handler.Requests[0].Uri.Query.Should().Contain("order=version.desc").And.Contain("limit=1");
        handler.Requests[2].Uri.Query.Should().Contain("version=lte.2").And.Contain($"offset={SupabaseCatalogRemote.PageSize}");
        handler.Requests[3].Uri.AbsolutePath.Should().EndWith("/catalog_programs");
    }

    [TestMethod]
    public async Task SupabaseRemote_TableLongerThanTheCeiling_Fails()
    {
        FakeHandler handler = new();
        SupabaseOptions options = new() { Url = "https://abc.supabase.co", PublishableKey = "k" };
        SupabaseCatalogRemote remote = new(new SupabaseRest(new HttpClient(handler), options, new NoTokens()), options);
        string page = "[" + string.Join(',', Enumerable.Range(0, SupabaseCatalogRemote.PageSize).Select(i => $$"""{"id":"w{{i}}","json":{"id":"w{{i}}"},"version":2}""")) + "]";
        for (int i = 0; i < SupabaseCatalogRemote.MaxPages + 1; i++)
        {
            handler.Enqueue(HttpStatusCode.OK, page);
        }

        Func<Task> act = () => remote.DownloadAsync(2);

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().HaveCount(SupabaseCatalogRemote.MaxPages);
    }

    [TestMethod]
    public async Task SupabaseRemote_NoVersionsPublished_ReturnsNull()
    {
        FakeHandler handler = new();
        handler.Enqueue(HttpStatusCode.OK, "[]");
        SupabaseOptions options = new() { Url = "https://abc.supabase.co", PublishableKey = "k" };
        SupabaseCatalogRemote remote = new(new SupabaseRest(new HttpClient(handler), options, new NoTokens()), options);

        (await remote.GetLatestVersionAsync()).Should().BeNull();
    }

    private (CatalogService Service, CatalogUpdateService Updates) Create(ICatalogCache cache, ICatalogRemote? remote = null, TimeProvider? time = null, SupabaseOptions? options = null)
    {
        CatalogService service = new(new BundledSource(), new FakeCustomProgramRepository(), _log.For<CatalogService>(), _messenger, cache, options);
        var enrollments = TestEnrollments.Create(new FakeTrackingRepository(Time), service, _messenger);
        DataChangeAnnouncer announcer = new(enrollments, service, _messenger);
        return (service, new(remote ?? _remote, service, announcer, time ?? Time, _log.For<CatalogUpdateService>()));
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Catalog", "Fixtures", name));

    private static IEnumerable<string> Workouts() => System.Text.Json.JsonDocument
        .Parse(Fixture("sample-workouts.json")).RootElement.EnumerateArray()
        .Select(e => e.GetRawText())
        .ToList();

    private static SupabaseOptions Supabase() => new() { Url = "https://abc.supabase.co", PublishableKey = "sb_publishable_x" };

    private static string Program(string id, int revision, string name, string? hero = null) => Fixture("sample-program.json")
        .Replace("\"heroImage\": null", hero is null ? "\"heroImage\": null" : $"\"heroImage\": \"{hero}\"")
        .Replace("\"id\": \"sample\"", $"\"id\": \"{id}\"")
        .Replace("\"revision\": 1", $"\"revision\": {revision}")
        .Replace("\"name\": \"Sample\"", $"\"name\": \"{name}\"");

    private sealed class BundledSource : IBundledCatalogSource
    {
        public Task<string> ReadWorkoutsAsync() => Task.FromResult(Fixture("sample-workouts.json"));

        public Task<IReadOnlyList<string>> ReadProgramsAsync() => Task.FromResult<IReadOnlyList<string>>([Fixture("sample-program.json").Replace("\"heroImage\": null", "\"heroImage\": \"Assets/Heroes/sample.png\"")]);
    }

    private sealed class FakeRemote : ICatalogRemote
    {
        private int? _version;
        private RemoteCatalog? _catalog;

        public bool IsAvailable => true;

        public Exception? Fail { get; set; }

        public Exception? FailDownload { get; set; }

        public TaskCompletionSource? Gate { get; set; }

        public Action? OnDownload { get; set; }

        public int Checks { get; private set; }

        public int Downloads { get; private set; }

        public void Publish(int version, IEnumerable<string> workouts, IEnumerable<string> programs)
        {
            _version = version;
            _catalog = new(
                version,
                [.. workouts],
                [.. programs.Select(json => ToRow(json))]);
        }

        public async Task<int?> GetLatestVersionAsync(CancellationToken ct = default)
        {
            Checks++;
            if (Gate is { } gate)
            {
                await gate.Task;
            }

            return Fail is { } error ? throw error : _version;
        }

        public Task<RemoteCatalog> DownloadAsync(int version, CancellationToken ct = default)
        {
            Downloads++;
            OnDownload?.Invoke();
            return FailDownload is { } error ? Task.FromException<RemoteCatalog>(error) : Task.FromResult(_catalog!);
        }

        private static RemoteProgram ToRow(string json)
        {
            if (!CatalogParser.TryParseProgram(json, out var program, out _))
            {
                return new("garbage", 1, json);
            }

            return new(program.Id, program.Revision, json);
        }
    }

    private sealed class MemoryCache : ICatalogCache
    {
        public CachedCatalog? Saved { get; set; }

        public bool SaveFails { get; set; }

        public Task<CachedCatalog?> LoadAsync() => Task.FromResult(Saved);

        public Task SaveAsync(int version, string? workoutsJson, IReadOnlyList<CachedProgram> programs)
        {
            if (SaveFails)
            {
                return Task.FromException(new IOException("disk full"));
            }

            Saved = new(version, workoutsJson, [.. (Saved?.Programs ?? []).Where(p => !programs.Any(n => n.Id == p.Id && n.Revision == p.Revision)), .. programs]);
            return Task.CompletedTask;
        }
    }

    private sealed class NoTokens : IAccessTokenProvider
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task<string?> RefreshAfterRejectionAsync(string rejectedToken, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class ListLogger
    {
        public List<(LogLevel Level, string Text)> Entries { get; } = [];

        public ILogger<T> For<T>() => new Typed<T>(this);

        private sealed class Typed<T>(ListLogger owner) : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Entries.Add((logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
