namespace ShowUp.Core.Storage;

public interface ISyncedSettings
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);
}
