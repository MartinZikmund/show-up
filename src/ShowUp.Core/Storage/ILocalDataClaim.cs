namespace ShowUp.Core.Storage;

/// <summary>What a signed-out device holds, for the claim question. Settings don't count: every install has some.</summary>
public sealed record AnonymousProgress(int Programs, int CrossedWorkouts, int OtherEntries)
{
    public bool IsEmpty => Programs + CrossedWorkouts + OtherEntries == 0;
}

/// <summary>Rows made while signed out (<c>UserId</c> null) and handing them to an account at sign-in.</summary>
public interface ILocalDataClaim
{
    Task<AnonymousProgress> GetAnonymousProgressAsync();

    /// <summary>In one transaction, gives every live signed-out row of every synced table to <paramref name="userId"/>, stamped so it syncs.</summary>
    /// <returns>How many rows were claimed.</returns>
    Task<int> ClaimAsync(string userId);
}
