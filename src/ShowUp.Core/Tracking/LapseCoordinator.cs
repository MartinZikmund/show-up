using Microsoft.Extensions.Logging;
using ShowUp.Core.Entitlements;
using ShowUp.Core.Services;

namespace ShowUp.Core.Tracking;

/// <summary>
/// When Pro lapses with more programs running than Free allows, asks once which one stays primary;
/// the others turn read-only. Dismissing keeps the most recently started one. Nothing is deleted.
/// App-wide: one prompt however many windows are open, shown in the window attached last.
/// </summary>
public sealed class LapseCoordinator
{
    private readonly IActiveEnrollmentService _enrollments;
    private readonly IEntitlementService _entitlements;
    private readonly TimeProvider _time;
    private readonly ILogger<LapseCoordinator> _logger;
    private readonly object _gate = new();
    private readonly List<IPrimaryProgramPrompt> _prompts = [];
    private Task? _firstRefresh;
    private int _running;

    public LapseCoordinator(IActiveEnrollmentService enrollments, IEntitlementService entitlements, TimeProvider time, ILogger<LapseCoordinator> logger)
    {
        _enrollments = enrollments;
        _entitlements = entitlements;
        _time = time;
        _logger = logger;
        _entitlements.Changed += (_, _) => _ = CheckWhenSettledAsync();
    }

    /// <summary>
    /// Lets a window show the prompt, and on the first call checks once the store has answered: a cached
    /// "not Pro" at launch may be a subscription that renewed while the app was closed.
    /// </summary>
    /// <returns>Dispose when the window closes.</returns>
    public IDisposable Attach(IPrimaryProgramPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        lock (_gate)
        {
            _prompts.Add(prompt);
            _firstRefresh ??= RefreshQuietlyAsync();
        }

        _ = CheckWhenSettledAsync();
        return new Detacher(() =>
        {
            lock (_gate)
            {
                _prompts.Remove(prompt);
            }
        });
    }

    /// <returns>True when the prompt was shown.</returns>
    public async Task<bool> CheckAsync()
    {
        IPrimaryProgramPrompt? prompt;
        lock (_gate)
        {
            prompt = _prompts.LastOrDefault();
        }

        if (prompt is null || _entitlements.IsPro || Interlocked.Exchange(ref _running, 1) == 1)
        {
            return false;
        }

        try
        {
            var all = await _enrollments.GetAllAsync();
            if (all.Count <= FeatureGate.MaxActivePrograms(isPro: false) || await _enrollments.HasPrimaryChoiceAsync())
            {
                return false;
            }

            var suggested = (await _enrollments.GetPrimaryAsync())!.Id;
            var picked = await prompt.PickAsync(all, suggested);
            await _enrollments.SetPrimaryAsync(picked is { } id && all.Any(e => e.Id == id) ? id : suggested);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Choosing the primary program failed.");
            return false;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private async Task<bool> CheckWhenSettledAsync()
    {
        Task? first;
        lock (_gate)
        {
            first = _firstRefresh;
        }

        if (first is null)
        {
            return false;
        }

        await first;
        return await CheckAsync();
    }

    private async Task RefreshQuietlyAsync()
    {
        try
        {
            await _entitlements.RefreshAsync().WaitAsync(ExpiryWatch.RefreshTimeout, _time);
        }
        catch (TimeoutException)
        {
            // Store unreachable: the cached state decides, as offline.
            _logger.LogWarning("The store didn't answer within {Timeout} at launch; checking the lapse from the cached state.", ExpiryWatch.RefreshTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Refreshing Pro before the lapse check failed.");
        }
    }

    private sealed class Detacher(Action detach) : IDisposable
    {
        public void Dispose() => detach();
    }
}
