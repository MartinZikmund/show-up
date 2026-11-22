namespace ShowUp.Core.Catalog;

/// <summary>What the update flow needs from the catalog: its installed version and a way to take a download.</summary>
public interface ICatalogUpdateTarget
{
    /// <summary>The newer of the bundled and the cached catalog version.</summary>
    Task<int> GetInstalledVersionAsync();

    /// <summary>
    /// Validates the download and caches what passes; invalid programs are skipped, a workouts list that drops a known
    /// id is rejected whole. False when nothing newer was installed. Throws if the cache can't be written (nothing changed).
    /// </summary>
    Task<bool> ApplyAsync(RemoteCatalog remote);
}
