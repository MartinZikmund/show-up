using AwesomeAssertions;
using PublishCatalog;
using static ShowUp.Core.Tests.Publishing.Catalogs;

namespace ShowUp.Core.Tests.Publishing;

[TestClass]
public class CatalogPublisherTests
{
    private const string Url = "https://abc.supabase.co";
    private const string SecretKey = "sb_secret_TOPSECRET";
    private static readonly LocalWorkout[] _workouts = [Workout("a"), Workout("b")];

    private static (CatalogPublisher Publisher, FakeSupabaseHandler Handler, StringWriter Output) Create(ServerState state)
    {
        FakeSupabaseHandler handler = new(state);
        StringWriter output = new();
        return (new(new(new(handler), Url, SecretKey), output), handler, output);
    }

    [TestMethod]
    public async Task RunAsync_Changes_WritesWorkoutsThenProgramsThenVersionLast()
    {
        var (publisher, handler, _) = Create(Server(3, _workouts, [Program("p", 1)]));

        var code = await publisher.RunAsync(Local([.. _workouts, Workout("c")], [Program("p", 1), Program("p", 2, "Fixed")]), dryRun: false);

        code.Should().Be(CatalogPublisher.Published);
        handler.Writes.Should().Equal("catalog_workouts", "catalog_programs", "catalog_versions");
    }

    [TestMethod]
    public async Task RunAsync_Changes_BumpsOnlyNewAndChangedWorkoutsAndNewProgramRevisions()
    {
        var (publisher, handler, _) = Create(Server(3, _workouts, [Program("p", 1)]));

        await publisher.RunAsync(
            Local([Workout("a"), Workout("b", "Renamed"), Workout("c")], [Program("p", 1), Program("p", 2, "Fixed")]),
            dryRun: false);

        var writes = handler.Requests.Where(r => r.Method == HttpMethod.Post).ToList();
        writes[0].Body.Should().Contain("\"id\":\"b\"").And.Contain("\"id\":\"c\"").And.NotContain("\"id\":\"a\"");
        writes[0].Body.Split("\"version\":4").Length.Should().Be(3);
        writes[0].Request.Headers.GetValues("Prefer").Should().ContainSingle().Which.Should().Contain("merge-duplicates");
        writes[1].Body.Should().Contain("\"revision\":2").And.NotContain("\"revision\":1");
        writes[1].Request.Headers.GetValues("Prefer").Should().ContainSingle().Which.Should().Contain("ignore-duplicates");
        writes[2].Body.Should().Be("[{\"version\":4}]");
    }

    [TestMethod]
    public async Task RunAsync_OnlyAProgramChanged_LeavesEveryWorkoutRowAlone()
    {
        var (publisher, handler, _) = Create(Server(3, _workouts, [Program("p", 1)]));

        await publisher.RunAsync(Local(_workouts, [Program("p", 1), Program("p", 2, "Fixed")]), dryRun: false);

        handler.Writes.Should().Equal("catalog_programs", "catalog_versions");
    }

    [TestMethod]
    public async Task RunAsync_NothingChanged_WritesNothingAndDoesNotBumpTheVersion()
    {
        var (publisher, handler, output) = Create(Server(3, _workouts, [Program("p", 1)]));

        var code = await publisher.RunAsync(Local(_workouts, [Program("p", 1)]), dryRun: false);

        code.Should().Be(CatalogPublisher.Published);
        handler.Writes.Should().BeEmpty();
        output.ToString().Should().Contain("no new version");
    }

    [TestMethod]
    public async Task RunAsync_DryRun_PrintsThePlanAndWritesNothing()
    {
        var (publisher, handler, output) = Create(Server(3, _workouts, []));

        var code = await publisher.RunAsync(Local([.. _workouts, Workout("c")], [Program("p", 1)]), dryRun: true);

        code.Should().Be(CatalogPublisher.Published);
        handler.Writes.Should().BeEmpty();
        output.ToString().Should().Contain("version 4").And.Contain("+ c").And.Contain("add p revision 1");
    }

    [TestMethod]
    public async Task RunAsync_ChangedPublishedRevision_RefusesBeforeWriting()
    {
        var (publisher, handler, output) = Create(Server(3, _workouts, [Program("p", 1, "Typo")]));

        var code = await publisher.RunAsync(Local(_workouts, [Program("p", 1, "Fixed")]), dryRun: false);

        code.Should().Be(CatalogPublisher.Refused);
        handler.Writes.Should().BeEmpty();
        output.ToString().Should().Contain("Bump");
    }

    [TestMethod]
    public async Task RunAsync_RemovedWorkoutId_RefusesBeforeWriting()
    {
        var (publisher, handler, _) = Create(Server(3, _workouts, []));

        var code = await publisher.RunAsync(Local([Workout("a")], []), dryRun: false);

        code.Should().Be(CatalogPublisher.Refused);
        handler.Writes.Should().BeEmpty();
    }

    [TestMethod]
    public async Task RunAsync_InvalidLocalCatalog_FailsWithoutAnyRequest()
    {
        var (publisher, handler, _) = Create(EmptyServer);
        LocalCatalog invalid = new([], [], new HashSet<string>(), ["Program 'p': has no tracks."]);

        var code = await publisher.RunAsync(invalid, dryRun: false);

        code.Should().Be(CatalogPublisher.Refused);
        handler.Requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task RunAsync_Hero_UploadsBeforeAnyTableWrite()
    {
        HeroUpload hero = new("p-abc.webp", Path.GetTempFileName(), "image/webp", CatalogLoader.HeroPublicUrl(Url, "p-abc.webp"));
        var (publisher, handler, _) = Create(Server(1, _workouts, []));

        try
        {
            await publisher.RunAsync(Local(_workouts, [Program("p", 1, hero: hero)]), dryRun: false);
        }
        finally
        {
            File.Delete(hero.FilePath);
        }

        handler.Writes.Should().Equal("storage:p-abc.webp", "catalog_programs", "catalog_versions");
    }

    [TestMethod]
    public async Task RunAsync_HeroAlreadyUploaded_IsNotUploadedAgain()
    {
        HeroUpload hero = new("p-abc.webp", "missing.webp", "image/webp", CatalogLoader.HeroPublicUrl(Url, "p-abc.webp"));
        var (publisher, handler, _) = Create(Server(1, _workouts, []));
        handler.HeroExists = true;

        await publisher.RunAsync(Local(_workouts, [Program("p", 1, hero: hero)]), dryRun: false);

        handler.Writes.Should().Equal("catalog_programs", "catalog_versions");
    }

    [TestMethod]
    public async Task RunAsync_ServerError_FailsAndSkipsTheVersionRow()
    {
        var (_, handler, _) = Create(Server(1, _workouts, []));
        FailingHandler failing = new(handler);
        StringWriter log = new();
        CatalogPublisher flaky = new(new(new(failing), Url, SecretKey), log);

        var code = await flaky.RunAsync(Local([.. _workouts, Workout("c")], [Program("p", 1)]), dryRun: false);

        code.Should().Be(CatalogPublisher.Failed);
        handler.Writes.Should().Equal("catalog_workouts");
        log.ToString().Should().Contain("write programs");
    }

    [TestMethod]
    public async Task RunAsync_Requests_SendTheKeyInApikeyOnlyAndNeverPrintIt()
    {
        var (publisher, handler, output) = Create(Server(1, _workouts, []));

        await publisher.RunAsync(Local([.. _workouts, Workout("c")], []), dryRun: false);

        handler.Requests.Should().OnlyContain(r => r.Request.Headers.GetValues("apikey").Single() == SecretKey && r.Request.Headers.Authorization == null);
        output.ToString().Should().NotContain(SecretKey);
    }

    [TestMethod]
    public async Task RunAsync_ServerPagesShorterThanTheLimit_AreStillReadToTheEnd()
    {
        LocalWorkout[] many = [.. Enumerable.Range(0, 5).Select(i => Workout($"w{i}"))];
        var (publisher, handler, output) = Create(Server(1, many, []));
        handler.PageCap = 2;

        var code = await publisher.RunAsync(Local(many, []), dryRun: false);

        code.Should().Be(CatalogPublisher.Published);
        handler.Writes.Should().BeEmpty("all five workouts were read, so nothing looks removed or new");
        output.ToString().Should().Contain("no new version");
    }

    [TestMethod]
    public async Task RunAsync_PublishedProgramWithoutLocalFile_WarnsButPublishes()
    {
        var (publisher, handler, output) = Create(Server(1, _workouts, [Program("old", 1)]));

        var code = await publisher.RunAsync(Local([.. _workouts, Workout("c")], []), dryRun: false);

        code.Should().Be(CatalogPublisher.Published);
        output.ToString().Should().Contain("Warning: Program 'old' is published");
        handler.Writes.Should().Contain("catalog_versions");
    }

    [TestMethod]
    public async Task RunAsync_HeroUpload_IsCachedForAYearAsImmutable()
    {
        HeroUpload hero = new("p-abc.webp", Path.GetTempFileName(), "image/webp", CatalogLoader.HeroPublicUrl(Url, "p-abc.webp"));
        var (publisher, handler, _) = Create(Server(1, _workouts, []));

        try
        {
            await publisher.RunAsync(Local(_workouts, [Program("p", 1, hero: hero)]), dryRun: false);
        }
        finally
        {
            File.Delete(hero.FilePath);
        }

        var upload = handler.Requests.Single(r => r.Uri.AbsolutePath.Contains("/storage/v1/object/catalog-heroes/"));
        upload.Request.Headers.GetValues("cache-control").Should().ContainSingle().Which.Should().Be("max-age=31536000, immutable");
    }

    // Lets everything through except writes to catalog_programs.
    private sealed class FailingHandler(FakeSupabaseHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.RequestUri!.AbsolutePath.EndsWith("catalog_programs") && request.Method == HttpMethod.Post
                ? Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError) { Content = new StringContent("boom") })
                : base.SendAsync(request, cancellationToken);
    }

    [TestMethod]
    public async Task RunAsync_OrphanRowsFromAFailedRun_InsertsTheMissingVersionRow()
    {
        var (publisher, handler, output) = Create(Server(3, _workouts, [], maxRowVersion: 4));

        var code = await publisher.RunAsync(Local(_workouts, []), dryRun: false);

        code.Should().Be(CatalogPublisher.Published);
        handler.Writes.Should().Equal("catalog_versions");
        handler.Requests.Single(r => r.Method == HttpMethod.Post).Body.Should().Be("[{\"version\":4}]");
        output.ToString().Should().Contain("recovering version 4");
    }
}
