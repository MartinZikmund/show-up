namespace ShowUp.Core.Catalog;

public interface IBundledCatalogSource
{
    Task<string> ReadWorkoutsAsync();

    Task<IReadOnlyList<string>> ReadProgramsAsync();
}
