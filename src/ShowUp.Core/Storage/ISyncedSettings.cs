namespace ShowUp.Core.Storage;

public interface ISyncedSettings
{
    Task<string?> GetAsync(string key);

    /// <summary>Newest value of every setting whose key starts with <paramref name="prefix"/>, by full key.</summary>
    Task<IReadOnlyDictionary<string, string>> GetByPrefixAsync(string prefix);

    Task SetAsync(string key, string value);

    /// <summary>
    /// Stores a default only when the key has no value, as the oldest possible version: any real choice, made here or
    /// pulled from another device, wins over it.
    /// </summary>
    Task SeedAsync(string key, string value);
}
