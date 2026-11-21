using ShowUp.Core.Storage;

namespace ShowUp.Services.Storage;

public sealed class AnonymousUserScope : IUserScope
{
    public string? CurrentUserId => null;
}
