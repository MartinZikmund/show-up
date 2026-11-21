namespace ShowUp.Core.Storage;

/// <summary>A setting that follows the user across devices; one per <see cref="Key"/> (<c>k:{key}</c>).</summary>
public sealed class SyncedSettingEntity : SyncableEntity
{
    public string SubjectKey { get; set; } = "";

    public string Key { get; set; } = "";

    public string Value { get; set; } = "";
}
