namespace ShowUp.Core.Catalog;

public sealed partial record CachedProgram(string Id, int Revision, int Version, string Json);

/// <param name="WorkoutsJson">The downloaded workouts array, or null while the bundled ones are still current.</param>
public sealed record CachedCatalog(int Version, string? WorkoutsJson, IReadOnlyList<CachedProgram> Programs);

/// <summary>Downloaded catalog JSON kept on the device. Programs are never deleted: enrollments pin their revision.</summary>
public interface ICatalogCache
{
    Task<CachedCatalog?> LoadAsync();

    /// <summary>One transaction: the version and workouts are replaced, the programs added (or replaced by id and revision).</summary>
    Task SaveAsync(int version, string? workoutsJson, IReadOnlyList<CachedProgram> programs);
}
