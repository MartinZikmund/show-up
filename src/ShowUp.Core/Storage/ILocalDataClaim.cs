namespace ShowUp.Core.Storage;

/// <summary>Rows made while signed out (<c>UserId</c> null) and handing them to an account on first sign-in.</summary>
public interface ILocalDataClaim
{
    /// <summary>Live signed-out rows other than settings (every install has those), i.e. something worth asking about.</summary>
    Task<bool> HasAnonymousProgressAsync();

    /// <summary>In one transaction, gives every live signed-out row of every synced table to <paramref name="userId"/>, stamped so it syncs.</summary>
    /// <returns>How many rows were claimed.</returns>
    Task<int> ClaimAsync(string userId);
}
