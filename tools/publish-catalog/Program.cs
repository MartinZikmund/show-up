using PublishCatalog;

// Usage: PublishCatalog [--catalog <dir>] [--dry-run]
// Env:   SUPABASE_URL, SUPABASE_SECRET_KEY (never printed)
string? catalogDir = null;
bool dryRun = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--dry-run":
            dryRun = true;
            break;
        case "--catalog" when i + 1 < args.Length:
            catalogDir = args[++i];
            break;
        default:
            Console.Error.WriteLine($"Unknown argument '{args[i]}'. Usage: PublishCatalog [--catalog <dir>] [--dry-run]");
            return CatalogPublisher.Failed;
    }
}

var url = Environment.GetEnvironmentVariable("SUPABASE_URL");
var key = Environment.GetEnvironmentVariable("SUPABASE_SECRET_KEY");
if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key) || !Uri.TryCreate(url, UriKind.Absolute, out _))
{
    Console.Error.WriteLine("Set SUPABASE_URL (https://<ref>.supabase.co) and SUPABASE_SECRET_KEY.");
    return CatalogPublisher.Failed;
}

catalogDir ??= FindCatalogDir();
if (catalogDir is null || !File.Exists(Path.Combine(catalogDir, "workouts.json")))
{
    Console.Error.WriteLine("Could not find the catalog folder; pass --catalog <dir>.");
    return CatalogPublisher.Failed;
}

var local = CatalogLoader.Load(catalogDir, url);
using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };
return await new CatalogPublisher(new(http, url, key), Console.Out).RunAsync(local, dryRun);

static string? FindCatalogDir()
{
    for (DirectoryInfo? dir = new(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "catalog");
        if (File.Exists(Path.Combine(candidate, "workouts.json")))
        {
            return candidate;
        }
    }

    return null;
}
