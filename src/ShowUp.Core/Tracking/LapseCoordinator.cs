using Microsoft.Extensions.Logging;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Services;

namespace ShowUp.Core.Tracking;

/// <summary>
/// When Pro lapses with more programs running than Free allows, asks once which one stays primary;
/// the others turn read-only. Dismissing keeps the most recently started one. Nothing is deleted.
/// </summary>
public sealed class LapseCoordinator(
    IActiveEnrollmentService enrollments,
    IEntitlementService entitlements,
    IPrimaryProgramPrompt prompt,
    ILogger<LapseCoordinator> logger)
{
    private int _running;

    /// <returns>True when the prompt was shown.</returns>
    public async Task<bool> CheckAsync()
    {
        if (entitlements.IsPro || Interlocked.Exchange(ref _running, 1) == 1)
        {
            return false;
        }

        try
        {
            var all = await enrollments.GetAllAsync();
            if (all.Count <= FeatureGate.MaxActivePrograms(isPro: false) || await enrollments.HasPrimaryChoiceAsync())
            {
                return false;
            }

            var suggested = (await enrollments.GetPrimaryAsync())!.Id;
            var picked = await prompt.PickAsync(all, suggested);
            await enrollments.SetPrimaryAsync(picked is { } id && all.Any(e => e.Id == id) ? id : suggested);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Choosing the primary program failed.");
            return false;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>Runs <see cref="CheckAsync"/> whenever Pro may have lapsed; dispose to stop.</summary>
    public IDisposable Watch()
    {
        EventHandler handler = (_, _) => _ = CheckAsync();
        entitlements.Changed += handler;
        return new Unsubscriber(() => entitlements.Changed -= handler);
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
