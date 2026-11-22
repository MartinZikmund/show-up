using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ShowUp.Core.Catalog;

namespace PublishCatalog;

public sealed partial record LocalWorkout(string Id, JsonElement Json);

/// <summary>A program as it will be published: <see cref="Json"/> already carries the Storage URL of its hero.</summary>
public sealed partial record LocalProgram(string Id, int Revision, JsonElement Json, HeroUpload? Hero);

public sealed record HeroUpload(string ObjectName, string FilePath, string ContentType, string PublicUrl);

public sealed record LocalCatalog(
    IReadOnlyList<LocalWorkout> Workouts,
    IReadOnlyList<LocalProgram> Programs,
    IReadOnlySet<string> LockedWorkoutIds,
    IReadOnlyList<string> Errors);

public static class CatalogLoader
{
    public const string HeroBucket = "catalog-heroes";
    public const int MaxHeroBytes = 2 * 1024 * 1024;

    private static readonly Dictionary<string, string> _mimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".webp"] = "image/webp",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
    };

    public static string HeroPublicUrl(string supabaseUrl, string objectName) =>
        $"{supabaseUrl.TrimEnd('/')}/storage/v1/object/public/{HeroBucket}/{objectName}";

    /// <summary>Reads and validates the folder with the same parser and validator the app uses. Never throws on bad content.</summary>
    public static LocalCatalog Load(string catalogDir, string supabaseUrl)
    {
        List<string> errors = new();
        List<LocalWorkout> workouts = new();
        List<LocalProgram> programs = new();

        var workoutsText = File.ReadAllText(Path.Combine(catalogDir, "workouts.json"));
        IReadOnlyList<Workout> parsedWorkouts = [];
        try
        {
            parsedWorkouts = CatalogParser.ParseWorkouts(workoutsText);
            workouts.AddRange(JsonDocument.Parse(workoutsText).RootElement.EnumerateArray()
                .Select(e => new LocalWorkout(e.GetProperty("id").GetString()!, e)));
        }
        catch (JsonException ex)
        {
            errors.Add($"workouts.json: {ex.Message}");
        }

        List<WorkoutProgram> parsedPrograms = new();
        var host = new Uri(supabaseUrl).Host;
        foreach (var file in Directory.GetFiles(Path.Combine(catalogDir, "programs"), "*.json").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            var text = File.ReadAllText(file);
            if (!CatalogParser.TryParseProgram(text, out var program, out var error))
            {
                errors.Add($"{name}: {error}");
                continue;
            }

            parsedPrograms.Add(program);
            var (json, hero) = PublishedJson(text, catalogDir, supabaseUrl, name, errors);
            if (hero is not null && !HeroImagePolicy.IsAllowed(hero.PublicUrl, host))
            {
                errors.Add($"{name}: hero URL '{hero.PublicUrl}' would be refused by the app (needs https on the Supabase host).");
            }

            programs.Add(new(program.Id, program.Revision, json, hero));
        }

        errors.AddRange(CatalogValidator.Validate(new(1, parsedWorkouts, parsedPrograms)));

        var lockFile = Path.Combine(catalogDir, "workout-ids.lock");
        HashSet<string> locked = File.Exists(lockFile)
            ? [.. File.ReadAllLines(lockFile).Select(l => l.Trim()).Where(l => l.Length > 0)]
            : [];

        return new(workouts, programs, locked, errors);
    }

    // Bundled heroImage paths whose file sits in catalog/heroes/ become public Storage URLs in the published copy only.
    private static (JsonElement Json, HeroUpload? Hero) PublishedJson(string text, string catalogDir, string supabaseUrl, string fileName, List<string> errors)
    {
        var node = JsonNode.Parse(text)!.AsObject();
        HeroUpload? hero = null;

        if (node["heroImage"] is JsonValue value
            && value.TryGetValue<string>(out var path)
            && !string.IsNullOrEmpty(path)
            && HeroImagePolicy.IsAllowed(path, null)
            && Path.Combine(catalogDir, "heroes", Path.GetFileName(path)) is var file
            && File.Exists(file))
        {
            hero = PrepareHero(file, supabaseUrl, fileName, errors);
            if (hero is not null)
            {
                node["heroImage"] = hero.PublicUrl;
            }
        }

        return (JsonDocument.Parse(node.ToJsonString()).RootElement, hero);
    }

    private static HeroUpload? PrepareHero(string file, string supabaseUrl, string programFile, List<string> errors)
    {
        var ext = Path.GetExtension(file);
        if (!_mimeTypes.TryGetValue(ext, out var mime))
        {
            errors.Add($"{programFile}: hero '{Path.GetFileName(file)}' must be .webp, .png or .jpg.");
            return null;
        }

        var bytes = File.ReadAllBytes(file);
        if (bytes.Length > MaxHeroBytes)
        {
            errors.Add($"{programFile}: hero '{Path.GetFileName(file)}' is over {MaxHeroBytes / 1024 / 1024} MiB (the bucket limit).");
            return null;
        }

        // The content hash in the name makes the object immutable: a new image is a new URL, so caches never go stale.
        var stem = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
        if (stem.Length == 0 || !stem.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            errors.Add($"{programFile}: hero file name '{Path.GetFileName(file)}' may only use letters, digits, '-' and '_'.");
            return null;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes))[..12];
        var objectName = $"{stem}-{hash}{ext.ToLowerInvariant()}";
        return new(objectName, file, mime, HeroPublicUrl(supabaseUrl, objectName));
    }
}
