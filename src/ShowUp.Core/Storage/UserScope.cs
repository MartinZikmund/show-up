namespace ShowUp.Core.Storage;

/// <summary>The live <see cref="IUserScope"/>; only the account flow switches it.</summary>
public sealed class UserScope : IUserScope
{
    private volatile string? _userId;

    public string? CurrentUserId => _userId;

    public event EventHandler? Changed;

    /// <returns>False when <paramref name="userId"/> already was the current one.</returns>
    public bool Set(string? userId)
    {
        if (_userId == userId)
        {
            return false;
        }

        _userId = userId;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
