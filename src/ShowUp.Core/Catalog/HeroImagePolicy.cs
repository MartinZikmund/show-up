namespace ShowUp.Core.Catalog;

/// <summary>Which <see cref="WorkoutProgram.HeroImage"/> values may be loaded: a bundled asset path, or an https URL on the Supabase host.</summary>
public static class HeroImagePolicy
{
    public static bool IsAllowed(string? heroImage, string? allowedHost)
    {
        if (string.IsNullOrEmpty(heroImage) || !(heroImage.Contains("://", StringComparison.Ordinal) || heroImage.StartsWith("//", StringComparison.Ordinal)))
        {
            return true;
        }

        return allowedHost is not null
            && Uri.TryCreate(heroImage, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, allowedHost, StringComparison.OrdinalIgnoreCase);
    }
}
