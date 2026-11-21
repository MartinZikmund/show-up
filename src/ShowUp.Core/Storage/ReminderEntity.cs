namespace ShowUp.Core.Storage;

/// <summary>A daily workout reminder.</summary>
public sealed class ReminderEntity : SyncableEntity
{
    public int MinutesOfDay { get; set; }

    /// <summary>Bit 0 = Monday ... bit 6 = Sunday.</summary>
    public int DaysMask { get; set; }

    public bool Enabled { get; set; }
}
