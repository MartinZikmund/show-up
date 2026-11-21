namespace ShowUp.Core.Infrastructure;

public interface IAppUpdater
{
    Task EnsureAppUpToDateAsync();
}
