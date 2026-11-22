namespace ShowUp.Core.Sync;

/// <summary>What the account card shows about sync, and its "Sync now".</summary>
public interface ISyncStatus
{
    SyncState State { get; }

    DateTimeOffset? LastSyncedAt { get; }

    /// <summary>On the UI thread.</summary>
    event EventHandler? Changed;

    /// <summary>Rows of the signed-in user that sync would send but the server hasn't confirmed (false when sync is off or paused).</summary>
    Task<bool> HasUnsyncedChangesAsync();

    Task SyncNowAsync();
}
