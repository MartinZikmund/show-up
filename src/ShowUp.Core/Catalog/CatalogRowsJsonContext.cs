using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShowUp.Core.Catalog;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(CatalogVersionRow))]
[JsonSerializable(typeof(CatalogWorkoutRow))]
[JsonSerializable(typeof(CatalogProgramRow))]
internal sealed partial class CatalogRowsJsonContext : JsonSerializerContext
{
}

internal sealed record CatalogVersionRow(int Version);

internal sealed partial record CatalogWorkoutRow(string Id, JsonElement Json, int Version);

internal sealed partial record CatalogProgramRow(string Id, int Revision, JsonElement Json, int Version);
