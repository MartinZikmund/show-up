using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShowUp.Core.Storage;

namespace ShowUp.Core.Sync;

/// <summary>The server twin of a <see cref="SyncableEntity"/> (docs/supabase.md, column mapping).</summary>
public abstract partial class SyncRow
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Set by the server only; never sent (null on every pushed row, so all rows still share one key set).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ServerUpdatedAt { get; set; }
}

public sealed partial class EnrollmentRow : SyncRow
{
    public string ProgramId { get; set; } = "";

    public int ProgramRevision { get; set; }

    public string TrackId { get; set; } = "";

    public int StartDayNumber { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public EnrollmentEndReason? EndReason { get; set; }
}

public sealed partial class CompletionRow : SyncRow
{
    public Guid EnrollmentId { get; set; }

    public int DayIndex { get; set; }

    public int SessionIndex { get; set; }

    public int CompletedOnDayNumber { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public string? InkJson { get; set; }
}

public sealed partial class ScheduleShiftRow : SyncRow
{
    public Guid EnrollmentId { get; set; }

    public int FromDayNumber { get; set; }

    public int Days { get; set; }
}

public sealed partial class BonusWorkoutRow : SyncRow
{
    public string? WorkoutId { get; set; }

    public string Title { get; set; } = "";

    public int DayNumber { get; set; }

    public int? DurationMinutes { get; set; }
}

public sealed partial class NoteRow : SyncRow
{
    public Guid EnrollmentId { get; set; }

    public int? DayIndex { get; set; }

    public int? SessionIndex { get; set; }

    public string SubjectKey { get; set; } = "";

    public string Text { get; set; } = "";
}

public sealed partial class MetricEntryRow : SyncRow
{
    public Guid? EnrollmentId { get; set; }

    public int? DayIndex { get; set; }

    public int? SessionIndex { get; set; }

    public Guid? BonusWorkoutId { get; set; }

    public string SubjectKey { get; set; } = "";

    public string WorkoutId { get; set; } = "";

    public string MetricId { get; set; } = "";

    public double? Value { get; set; }

    public string? TextValue { get; set; }
}

public sealed partial class ReminderRow : SyncRow
{
    public int MinutesOfDay { get; set; }

    public int DaysMask { get; set; }

    public bool Enabled { get; set; }
}

public sealed partial class CustomProgramRow : SyncRow
{
    public string SubjectKey { get; set; } = "";

    public int Revision { get; set; }

    public string ProgramJson { get; set; } = "";
}

public sealed partial class SyncedSettingRow : SyncRow
{
    public string SubjectKey { get; set; } = "";

    public string Key { get; set; } = "";

    public string Value { get; set; } = "";
}

/// <summary>UTC, whole microseconds: exactly what Postgres stores, so a row reads back equal.</summary>
public sealed class MicrosecondTimestampConverter : JsonConverter<DateTimeOffset>
{
    public static string Format(DateTimeOffset value) =>
        Timestamps.Truncate(value).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Format(value));
}

// Every key is written, nulls included (PGRST102 wants one key set per batch; a null must reach the server to clear a column).
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    Converters = [typeof(MicrosecondTimestampConverter)])]
[JsonSerializable(typeof(EnrollmentRow))]
[JsonSerializable(typeof(CompletionRow))]
[JsonSerializable(typeof(ScheduleShiftRow))]
[JsonSerializable(typeof(BonusWorkoutRow))]
[JsonSerializable(typeof(NoteRow))]
[JsonSerializable(typeof(MetricEntryRow))]
[JsonSerializable(typeof(ReminderRow))]
[JsonSerializable(typeof(CustomProgramRow))]
[JsonSerializable(typeof(SyncedSettingRow))]
public sealed partial class SyncJsonContext : JsonSerializerContext
{
}
