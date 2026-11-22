namespace ShowUp.Core.Catalog;

/// <summary>
/// Which <see cref="WorkoutProgram.HeroImage"/> values may be loaded: a plain relative path to a bundled asset
/// (<c>Assets/heroes/x.webp</c>), or an absolute https URL on the Supabase host. Anything else, including
/// <c>ms-appx:</c> URIs, malformed lookalikes such as <c>https:/host/x</c> and rooted paths, is refused.
/// </summary>
public static class HeroImagePolicy
{
    public static bool IsAllowed(string? heroImage, string? allowedHost)
    {
        if (string.IsNullOrEmpty(heroImage) || IsBundledPath(heroImage))
        {
            return true;
        }

        return allowedHost is not null
            && Uri.TryCreate(heroImage, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, allowedHost, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBundledPath(string value) =>
        Uri.TryCreate(value, UriKind.Relative, out _)
        && !value.Contains(':')
        && !value.StartsWith('/')
        && !value.StartsWith('\\');
}
