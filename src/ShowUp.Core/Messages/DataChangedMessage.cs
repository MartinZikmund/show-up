namespace ShowUp.Core.Messages;

/// <summary>Sent when stored data changed behind the services' backs (a sync pull, a catalog update): cached reads are stale.</summary>
public sealed record DataChangedMessage;
