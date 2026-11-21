using SQLite;

namespace ShowUp.Core.Storage;

/// <summary>One saved revision of a user-built program; the program id lives in <see cref="SubjectKey"/> (<c>c:{programId}</c>).</summary>
public sealed class CustomProgramEntity : SyncableEntity
{
    public string SubjectKey { get; set; } = "";

    public int Revision { get; set; }

    public string ProgramJson { get; set; } = "";

    /// <summary>The program id, or null when <see cref="SubjectKey"/> is not a well-formed <c>c:{id}</c>.</summary>
    [Ignore]
    public string? ProgramId => SubjectKey.Length > 2 && SubjectKey.StartsWith("c:", StringComparison.Ordinal) ? SubjectKey[2..] : null;
}
