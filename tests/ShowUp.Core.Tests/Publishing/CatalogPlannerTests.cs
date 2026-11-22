using AwesomeAssertions;
using PublishCatalog;
using static ShowUp.Core.Tests.Publishing.Catalogs;

namespace ShowUp.Core.Tests.Publishing;

[TestClass]
public class CatalogPlannerTests
{
    private static readonly LocalWorkout[] _workouts = [Workout("a"), Workout("b")];

    [TestMethod]
    public void Plan_EmptyServer_PublishesVersionAfterTheBundledOneWithEverything()
    {
        var plan = CatalogPlanner.Plan(Local(_workouts, [Program("p", 1)]), EmptyServer);

        plan.IsRefused.Should().BeFalse();
        plan.NewVersion.Should().Be(2);
        plan.NewWorkouts.Should().Equal("a", "b");
        plan.NewPrograms.Should().ContainSingle();
    }

    [TestMethod]
    public void Plan_NothingChanged_HasNoChangesEvenIfKeyOrderAndNumbersDiffer()
    {
        LocalProgram local = new("p", 1, Json.Parse("""{"id":"p","revision":1,"n":30}"""), null);
        LocalProgram server = new("p", 1, Json.Parse("""{"n":30.0,"revision":1,"id":"p"}"""), null);

        var plan = CatalogPlanner.Plan(Local(_workouts, [local]), Server(4, _workouts, [server]));

        plan.IsRefused.Should().BeFalse();
        plan.HasChanges.Should().BeFalse();
    }

    [TestMethod]
    public void Plan_NewAndChangedWorkouts_AreListedAndBumpTheVersion()
    {
        var local = Local([Workout("a"), Workout("b", "Renamed"), Workout("c")], [], "a", "b");

        var plan = CatalogPlanner.Plan(local, Server(7, _workouts, []));

        plan.NewVersion.Should().Be(8);
        plan.NewWorkouts.Should().Equal("c");
        plan.ChangedWorkouts.Should().Equal("b");
        plan.HasChanges.Should().BeTrue();
    }

    [TestMethod]
    public void Plan_WorkoutMissingButInLock_IsRefused()
    {
        var plan = CatalogPlanner.Plan(Local([Workout("a")], [], "a", "b"), EmptyServer);

        plan.IsRefused.Should().BeTrue();
        plan.Errors.Should().ContainSingle().Which.Should().Contain("'b'").And.Contain("workout-ids.lock");
    }

    [TestMethod]
    public void Plan_WorkoutMissingButOnServer_IsRefused()
    {
        var plan = CatalogPlanner.Plan(Local([Workout("a")], [], "a"), Server(1, _workouts, []));

        plan.IsRefused.Should().BeTrue();
        plan.Errors.Should().ContainSingle().Which.Should().Contain("'b'").And.Contain("server");
    }

    [TestMethod]
    public void Plan_PublishedRevisionWithChangedJson_IsRefusedAskingForARevisionBump()
    {
        var plan = CatalogPlanner.Plan(
            Local(_workouts, [Program("p", 1, "Fixed")]),
            Server(2, _workouts, [Program("p", 1, "Typo")]));

        plan.IsRefused.Should().BeTrue();
        plan.Errors.Should().ContainSingle().Which.Should().Contain("'p'").And.Contain("Bump");
    }

    [TestMethod]
    public void Plan_NewRevision_AddsOnlyThatRevision()
    {
        var plan = CatalogPlanner.Plan(
            Local(_workouts, [Program("p", 2, "Fixed"), Program("q", 1)]),
            Server(2, _workouts, [Program("p", 1, "Typo"), Program("q", 1)]));

        plan.IsRefused.Should().BeFalse();
        plan.NewPrograms.Select(p => (p.Id, p.Revision)).Should().Equal(("p", 2));
    }

    [TestMethod]
    public void Plan_RevisionBelowPublished_IsRefused()
    {
        var plan = CatalogPlanner.Plan(
            Local(_workouts, [Program("p", 2)]),
            Server(2, _workouts, [Program("p", 3)]));

        plan.Errors.Should().ContainSingle().Which.Should().Contain("only go up");
    }

    [TestMethod]
    public void Plan_JsonWithDuplicateKeys_ComparesTheLastValue()
    {
        LocalProgram local = new("p", 1, Json.Parse("""{"id":"p","revision":1,"n":"old","n":"new"}"""), null);
        LocalProgram server = new("p", 1, Json.Parse("""{"id":"p","revision":1,"n":"new"}"""), null);

        var plan = CatalogPlanner.Plan(Local(_workouts, [local]), Server(2, _workouts, [server]));

        plan.IsRefused.Should().BeFalse();
        plan.HasChanges.Should().BeFalse();
    }

    [TestMethod]
    public void Plan_HeroOnNewProgram_IsPlannedForUpload()
    {
        HeroUpload hero = new("p-abc.webp", "p.webp", "image/webp", "https://x.supabase.co/storage/v1/object/public/catalog-heroes/p-abc.webp");

        var plan = CatalogPlanner.Plan(Local(_workouts, [Program("p", 1, hero: hero)]), Server(1, _workouts, []));

        plan.Heroes.Should().Equal(hero);
    }

    [TestMethod]
    public void Plan_RowsAboveTheRecordedVersion_RecoversThatVersionEvenWithNothingElseChanged()
    {
        var plan = CatalogPlanner.Plan(Local(_workouts, []), Server(3, _workouts, [], maxRowVersion: 4));

        plan.IsRefused.Should().BeFalse();
        plan.Recovering.Should().BeTrue();
        plan.NewVersion.Should().Be(4);
        plan.HasChanges.Should().BeTrue();
        CatalogPlanner.Describe(plan).Should().Contain("recovering version 4");
    }

    [TestMethod]
    public void Plan_RowsAtTheRecordedVersion_IsNotRecovery()
    {
        var plan = CatalogPlanner.Plan(Local(_workouts, []), Server(3, _workouts, [], maxRowVersion: 3));

        plan.Recovering.Should().BeFalse();
        plan.HasChanges.Should().BeFalse();
    }

    [TestMethod]
    public void Plan_ServerBehindTheBundledCatalog_NeverPlansTheBundledVersion()
    {
        var plan = CatalogPlanner.Plan(Local(_workouts, []), EmptyServer);

        plan.NewVersion.Should().BeGreaterThan(ShowUp.Core.Catalog.CatalogService.BundledCatalogVersion);
    }
}
