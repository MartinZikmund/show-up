using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ShowUp.Core.Catalog;

public static class CatalogParser
{
    /// <summary>Throws <see cref="JsonException"/> if any file is malformed; use the per-file methods to tolerate bad programs.</summary>
    public static WorkoutCatalog Parse(int version, string workoutsJson, IEnumerable<string> programJsons)
    {
        var workouts = ParseWorkouts(workoutsJson);
        var programs = programJsons
            .Select(json => JsonSerializer.Deserialize(json, CatalogJsonContext.Default.WorkoutProgram)
                ?? throw new JsonException("A program file is empty."))
            .ToList();

        return new WorkoutCatalog(version, workouts, programs);
    }

    public static IReadOnlyList<Workout> ParseWorkouts(string json) =>
        JsonSerializer.Deserialize(json, CatalogJsonContext.Default.WorkoutArray)
            ?? throw new JsonException("workouts.json is empty.");

    /// <summary>The <c>index.json</c> array of program file names.</summary>
    public static IReadOnlyList<string> ParseProgramIndex(string json) =>
        JsonSerializer.Deserialize(json, CatalogJsonContext.Default.StringArray)
            ?? throw new JsonException("index.json is empty.");

    public static bool TryParseProgram(string json, [NotNullWhen(true)] out WorkoutProgram? program, [NotNullWhen(false)] out string? error)
    {
        try
        {
            program = JsonSerializer.Deserialize(json, CatalogJsonContext.Default.WorkoutProgram);
            error = program is null ? "The program file is empty." : null;
        }
        catch (JsonException ex)
        {
            program = null;
            error = ex.Message;
        }

        return program is not null;
    }
}
