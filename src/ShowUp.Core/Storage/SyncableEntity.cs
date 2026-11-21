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
}
