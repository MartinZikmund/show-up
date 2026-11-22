using SQLite;

namespace ShowUp.Core.Storage;

public abstract class SyncableEntity
{
    [PrimaryKey]
    public Guid Id { get; set; }

    public string? UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public string DeviceId { get; set; } = "";

    /// <summary>Changed here since the server last confirmed it; every local write sets it, a confirmed push clears it.</summary>
    public bool SyncPending { get; set; } = true;
}
