namespace ShowUp.Core.Storage;

public interface ISyncedSettings
{
    Task<string?> GetAsync(string key);

    /// <summary>Newest value of every setting whose key starts with <paramref name="prefix"/>, by full key.</summary>
    Task<IReadOnlyDictionary<string, string>> GetByPrefixAsync(string prefix);

    Task SetAsync(string key, string value);
}
