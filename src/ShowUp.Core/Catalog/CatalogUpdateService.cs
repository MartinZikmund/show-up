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
    private long _lastCheckTicks = long.MinValue;

    public Task StartAsync() => CheckAsync();

    public Task ResumeAsync()
    {
        long last = Volatile.Read(ref _lastCheckTicks);
        return last != long.MinValue && time.GetUtcNow().UtcTicks - last < Interval.Ticks ? Task.CompletedTask : CheckAsync();
    }

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

                // Published while downloading: the rows may be a mix, so take none and look again next time.
                if (await remote.GetLatestVersionAsync() != version)
                {
                    logger.LogDebug("Catalog version moved during the download; trying again later.");
                    return;
                }

                if (await target.ApplyAsync(download))
                {
                    announcer.Announce();
                }
            }

            Volatile.Write(ref _lastCheckTicks, time.GetUtcNow().UtcTicks);
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
