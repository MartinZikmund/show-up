using System.Text.Json;

namespace ShowUp.Core.Catalog;

public static class CatalogParser
{
    public static WorkoutCatalog Parse(int version, string workoutsJson, IEnumerable<string> programJsons)
    {
        var workouts = JsonSerializer.Deserialize(workoutsJson, CatalogJsonContext.Default.WorkoutArray)
            ?? throw new JsonException("workouts.json is empty.");
        var programs = programJsons
            .Select(json => JsonSerializer.Deserialize(json, CatalogJsonContext.Default.WorkoutProgram)
                ?? throw new JsonException("A program file is empty."))
            .ToList();

        return new WorkoutCatalog(version, workouts, programs);
    }
}
