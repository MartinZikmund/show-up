using ShowUp.Core.Catalog;
using Windows.Storage;

namespace ShowUp.Services.Catalog;

public sealed class BundledCatalogSource : IBundledCatalogSource
{
    private const string Root = "ms-appx:///Assets/Catalog/";

    public Task<string> ReadWorkoutsAsync() => ReadAsync("workouts.json");

    public async Task<IReadOnlyList<string>> ReadProgramsAsync()
    {
        var names = CatalogParser.ParseProgramIndex(await ReadAsync("index.json"));
        List<string> programs = new(names.Count);
        foreach (var name in names)
        {
            programs.Add(await ReadAsync($"programs/{name}"));
        }

        return programs;
    }

    private static async Task<string> ReadAsync(string path)
    {
        var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(Root + path));
        return await FileIO.ReadTextAsync(file);
    }
}
