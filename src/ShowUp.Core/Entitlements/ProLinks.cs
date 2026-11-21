namespace ShowUp.Core.Entitlements;

/// <summary>Where the paywall's Terms and Privacy links go; store review wants both.</summary>
public sealed record ProLinks(Uri Terms, Uri Privacy);
