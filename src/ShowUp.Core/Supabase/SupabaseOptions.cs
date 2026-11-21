namespace ShowUp.Core.Supabase;

/// <summary>Bound from <c>Supabase:Url</c> and <c>Supabase:PublishableKey</c>. Empty means every Supabase feature is off.</summary>
public sealed class SupabaseOptions
{
    private readonly string _url = "";

    public string Url
    {
        get => _url;
        init => _url = value?.Trim().TrimEnd('/') ?? "";
    }

    /// <summary>The <c>sb_publishable_...</c> key. Never the secret or service-role key.</summary>
    public string PublishableKey { get; init; } = "";

    public bool IsConfigured =>
        PublishableKey.Length > 0 && Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri) && (uri.Scheme is "https" or "http");
}
