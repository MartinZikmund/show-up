namespace ShowUp.Core.Storage;

/// <summary>Whose rows the repositories read and stamp: the signed-in user's id, or null when signed out.</summary>
public interface IUserScope
{
    string? CurrentUserId { get; }

    /// <summary>Raised on whatever thread switched the user.</summary>
    event EventHandler? Changed;
}
