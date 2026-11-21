namespace ShowUp.Core.Storage;

public interface IUserScope
{
    string? CurrentUserId { get; }
}
