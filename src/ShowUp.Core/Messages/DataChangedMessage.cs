namespace ShowUp.Core.Messages;

/// <summary>
/// Stored data changed behind the services' backs (a sign-in, a claim, a sync pull, a catalog update): cached reads are stale.
/// Send it through <see cref="DataChangeAnnouncer"/>, which drops the service caches first.
/// </summary>
public sealed record DataChangedMessage;
