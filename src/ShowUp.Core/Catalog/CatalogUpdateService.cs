using Microsoft.Extensions.Logging;
using ShowUp.Core.Messages;

namespace ShowUp.Core.Catalog;

/// <summary>
/// Checks for a newer published catalog on start and at most every 12 hours on resume, in the background.
/// Offline or failing just keeps the catalog it has; see docs/catalog-delivery.md.
/// </summary>
public sealed class CatalogUpdateService(
    ICatalogRemote remote,
    ICatalogUpdateTarget target,
    DataChangeAnnouncer announcer,
    TimeProvider time,
    ILogger<CatalogUpdateService> logger)
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    private readonly SemaphoreSlim _running = new(1, 1);
    private DateTimeOffset? _lastCheck;

    public Task StartAsync() => CheckAsync();

    public Task ResumeAsync() => _lastCheck is { } last && time.GetUtcNow() - last < Interval ? Task.CompletedTask : CheckAsync();

    private async Task CheckAsync()
    {
        if (!remote.IsAvailable || !await _running.WaitAsync(0))
        {
            return;
        }

        try
        {
            int? latest = await remote.GetLatestVersionAsync();
            if (latest is { } version && version > await target.GetInstalledVersionAsync())
            {
                var download = await remote.DownloadAsync(version);
                if (await target.ApplyAsync(download))
                {
                    announcer.Announce();
                }
            }

            _lastCheck = time.GetUtcNow();
        }
        catch (Exception ex)
        {
            // Offline is normal; the next start or resume tries again.
            logger.LogDebug(ex, "Catalog update check failed; keeping the current catalog.");
        }
        finally
        {
            _running.Release();
        }
    }
}
