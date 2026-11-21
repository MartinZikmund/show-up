using System.Text.Json.Serialization;

namespace ShowUp.Core.Catalog;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Workout[]))]
[JsonSerializable(typeof(WorkoutProgram))]
public partial class CatalogJsonContext : JsonSerializerContext
{
}
