using CommunityToolkit.Mvvm.Messaging;
using ShowUp.Core.Catalog;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Messages;
using ShowUp.Core.Storage;

namespace ShowUp.Core.CustomPrograms;

public sealed class CustomProgramService(
    ICustomProgramRepository repository,
    ICatalogService catalog,
    IEntitlementService entitlements,
    IMessenger messenger,
    TimeProvider time) : ICustomProgramService
{
    public bool CanEdit => FeatureGate.CanUse(ProFeature.CustomSchedules, entitlements.IsPro);

    public async Task<(string ProgramId, int Revision)> SaveAsync(CustomProgramDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        EnsureCanEdit();

        var program = CustomProgramFactory.Create(draft, DateOnly.FromDateTime(time.GetLocalNow().DateTime));
        // Checked as the catalog will load it: against bundled workouts plus its own.
        var bundled = (await catalog.GetCatalogAsync()).Workouts.Where(w => !w.Id.StartsWith(SqliteCustomProgramRepository.IdPrefix, StringComparison.Ordinal)).ToList();
        var errors = CatalogValidator.Validate(new WorkoutCatalog(CatalogService.BundledCatalogVersion, bundled, [program]));
        if (errors.Count > 0)
        {
            throw new ArgumentException($"The schedule isn't valid: {string.Join(" ", errors)}", nameof(draft));
        }

        var saved = await repository.SaveNewRevisionAsync(CustomProgramFactory.Serialize(program), draft.ProgramId);
        catalog.InvalidateCustomPrograms();
        messenger.Send(new CustomProgramsChangedMessage(saved.ProgramId));
        return saved;
    }

    // No Pro check: it's the user's own data, so a lapse never stops them removing it.
    public async Task DeleteAsync(string programId)
    {
        await repository.DeleteAsync(programId);
        catalog.InvalidateCustomPrograms();
        messenger.Send(new CustomProgramsChangedMessage(programId));
    }

    public async Task<int> GetVersionNumberAsync(string programId, int revision)
    {
        var revisions = await repository.GetRevisionsAsync(programId);
        int index = revisions.ToList().IndexOf(revision);
        return index < 0 ? revisions.Count(r => r < revision) + 1 : index + 1;
    }

    private void EnsureCanEdit()
    {
        if (!CanEdit)
        {
            throw new CustomProgramLockedException();
        }
    }
}
