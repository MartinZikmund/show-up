namespace ShowUp.Core.Sync;

public interface ISyncStatus
{
    /// <summary>Rows of the signed-in user changed after the last successful push.</summary>
    Task<bool> HasUnsyncedChangesAsync();
}

/// <summary>Stands in until the sync engine lands. Signing out never deletes rows, so answering false loses nothing.</summary>
public sealed class NoSyncStatus : ISyncStatus
{
    public Task<bool> HasUnsyncedChangesAsync() => Task.FromResult(false);
}
