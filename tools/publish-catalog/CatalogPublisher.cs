namespace PublishCatalog;

/// <summary>Plans and applies a publish. The version row goes in last, so a client never sees a version without its rows.</summary>
public sealed class CatalogPublisher(SupabaseAdmin admin, TextWriter output)
{
    public const int Published = 0;
    public const int Refused = 1;
    public const int Failed = 2;

    public async Task<int> RunAsync(LocalCatalog local, bool dryRun, CancellationToken ct = default)
    {
        if (local.Errors.Count > 0)
        {
            output.WriteLine("The catalog is not valid:");
            foreach (var error in local.Errors)
            {
                output.WriteLine("  - " + error);
            }

            return Refused;
        }

        try
        {
            var plan = CatalogPlanner.Plan(local, await admin.ReadStateAsync(ct));
            output.WriteLine(CatalogPlanner.Describe(plan, local.Workouts.Count));
            if (plan.IsRefused)
            {
                return Refused;
            }

            if (!plan.HasChanges)
            {
                return Published;
            }

            if (dryRun)
            {
                output.WriteLine("Dry run: nothing was written.");
                return Published;
            }

            // Heroes first: the program rows point at them, and an upload failure then leaves the tables untouched.
            foreach (var hero in plan.Heroes)
            {
                if (!await admin.HeroExistsAsync(hero, ct))
                {
                    await admin.UploadHeroAsync(hero, ct);
                }
            }

            await admin.UpsertWorkoutsAsync(local.Workouts, plan.NewVersion, ct);
            await admin.InsertProgramsAsync(plan.NewPrograms, plan.NewVersion, ct);
            await admin.InsertVersionAsync(plan.NewVersion, ct);
            output.WriteLine($"Published catalog version {plan.NewVersion}.");
            return Published;
        }
        catch (PublishException ex)
        {
            output.WriteLine(ex.Message);
            return Failed;
        }
    }
}
