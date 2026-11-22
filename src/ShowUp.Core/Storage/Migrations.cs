using SQLite;

namespace ShowUp.Core.Storage;

// Each version is frozen DDL, never the live entity classes, so later versions can ALTER safely.
public static class Migrations
{
    public const int LatestVersion = 5;

    /// <param name="targetVersion">Stop after this version (tests build older schemas with it).</param>
    public static async Task ApplyAsync(SQLiteAsyncConnection connection, int targetVersion = LatestVersion)
    {
        int version = await connection.ExecuteScalarAsync<int>("PRAGMA user_version");

        if (version < 1 && targetVersion >= 1)
        {
            await connection.RunInTransactionAsync(V1);
        }

        if (version < 2 && targetVersion >= 2)
        {
            await connection.RunInTransactionAsync(V2);
        }

        if (version < 3 && targetVersion >= 3)
        {
            await connection.RunInTransactionAsync(V3);
        }

        if (version < 4 && targetVersion >= 4)
        {
            await connection.RunInTransactionAsync(V4);
        }

        if (version < 5 && targetVersion >= 5)
        {
            await connection.RunInTransactionAsync(V5);
        }
    }

    private static void V1(SQLiteConnection c)
    {
        c.Execute("""
            CREATE TABLE IF NOT EXISTS "EnrollmentEntity" (
                "Id" varchar(36) primary key not null,
                "UserId" varchar,
                "CreatedAt" bigint,
                "UpdatedAt" bigint,
                "DeletedAt" bigint,
                "DeviceId" varchar,
                "ProgramId" varchar,
                "ProgramRevision" integer,
                "TrackId" varchar,
                "StartDayNumber" integer,
                "EndedAt" bigint,
                "EndReason" integer
            )
            """);
        c.Execute("""
            CREATE TABLE IF NOT EXISTS "CompletionEntity" (
                "Id" varchar(36) primary key not null,
                "UserId" varchar,
                "CreatedAt" bigint,
                "UpdatedAt" bigint,
                "DeletedAt" bigint,
                "DeviceId" varchar,
                "EnrollmentId" varchar(36),
                "DayIndex" integer,
                "SessionIndex" integer,
                "CompletedOnDayNumber" integer,
                "CompletedAt" bigint
            )
            """);
        c.Execute("""CREATE INDEX IF NOT EXISTS "IX_Completion_Session" ON "CompletionEntity" ("EnrollmentId", "DayIndex", "SessionIndex")""");
        c.Execute("""
            CREATE TABLE IF NOT EXISTS "ScheduleShiftEntity" (
                "Id" varchar(36) primary key not null,
                "UserId" varchar,
                "CreatedAt" bigint,
                "UpdatedAt" bigint,
                "DeletedAt" bigint,
                "DeviceId" varchar,
                "EnrollmentId" varchar(36),
                "FromDayNumber" integer,
                "Days" integer
            )
            """);
        c.Execute("""CREATE INDEX IF NOT EXISTS "ScheduleShiftEntity_EnrollmentId" ON "ScheduleShiftEntity" ("EnrollmentId")""");
        c.Execute("""
            CREATE TABLE IF NOT EXISTS "BonusWorkoutEntity" (
                "Id" varchar(36) primary key not null,
                "UserId" varchar,
                "CreatedAt" bigint,
                "UpdatedAt" bigint,
                "DeletedAt" bigint,
                "DeviceId" varchar,
                "WorkoutId" varchar,
                "Title" varchar,
                "DayNumber" integer,
                "DurationMinutes" integer
            )
            """);
        c.Execute("PRAGMA user_version = 1");
    }

    // The user's own strokes for a drawn cross (InkCodec JSON); null = the generated marker X.
    private static void V2(SQLiteConnection c)
    {
        c.Execute("""ALTER TABLE "CompletionEntity" ADD COLUMN "InkJson" varchar""");
        c.Execute("PRAGMA user_version = 2");
    }

    // Notes (program, day, session) and logged workout results.
    private static void V3(SQLiteConnection c)
    {
        c.Execute("""
            CREATE TABLE IF NOT EXISTS "NoteEntity" (
                "Id" varchar(36) primary key not null,
                "UserId" varchar,
                "CreatedAt" bigint,
                "UpdatedAt" bigint,
                "DeletedAt" bigint,
                "DeviceId" varchar,
                "EnrollmentId" varchar(36),
                "DayIndex" integer,
                "SessionIndex" integer,
                "SubjectKey" varchar,
                "Text" varchar
            )
            """);
        c.Execute("""CREATE INDEX IF NOT EXISTS "IX_Note_Enrollment" ON "NoteEntity" ("EnrollmentId")""");
        c.Execute("""CREATE INDEX IF NOT EXISTS "IX_Note_Subject" ON "NoteEntity" ("SubjectKey")""");
        c.Execute("""
            CREATE TABLE IF NOT EXISTS "MetricEntryEntity" (
                "Id" varchar(36) primary key not null,
                "UserId" varchar,
                "CreatedAt" bigint,
                "UpdatedAt" bigint,
                "DeletedAt" bigint,
                "DeviceId" varchar,
                "EnrollmentId" varchar(36),
                "DayIndex" integer,
                "SessionIndex" integer,
                "BonusWorkoutId" varchar(36),
                "SubjectKey" varchar,
                "WorkoutId" varchar,
                "MetricId" varchar,
                "Value" float,
                "TextValue" varchar
            )
            """);
        c.Execute("""CREATE INDEX IF NOT EXISTS "IX_MetricEntry_Enrollment" ON "MetricEntryEntity" ("EnrollmentId")""");
        c.Execute("""CREATE INDEX IF NOT EXISTS "IX_MetricEntry_Bonus" ON "MetricEntryEntity" ("BonusWorkoutId")""");
        c.Execute("""CREATE INDEX IF NOT EXISTS "IX_MetricEntry_Subject" ON "MetricEntryEntity" ("SubjectKey", "MetricId")""");
        c.Execute("PRAGMA user_version = 3");
    }

    // Reminders, user-built programs (one row per revision) and settings that sync.
    private static void V4(SQLiteConnection c)
    {
        c.Execute("""
            CREATE TABLE IF NOT EXISTS "ReminderEntity" (
                "Id" varchar(36) primary key not null,
                "UserId" varchar,
                "CreatedAt" bigint,
                "UpdatedAt" bigint,
                "DeletedAt" bigint,
                "DeviceId" varchar,
                "MinutesOfDay" integer,
                "DaysMask" integer,
                "Enabled" integer
            )
            """);
        c.Execute("""CREATE INDEX IF NOT EXISTS "IX_Reminder_User" ON "ReminderEntity" ("UserId")""");
        c.Execute("""
            CREATE TABLE IF NOT EXISTS "CustomProgramEntity" (
                "Id" varchar(36) primary key not null,
                "UserId" varchar,
                "CreatedAt" bigint,
                "UpdatedAt" bigint,
                "DeletedAt" bigint,
                "DeviceId" varchar,
                "SubjectKey" varchar,
                "Revision" integer,
                "ProgramJson" varchar
            )
            """);
        c.Execute("""CREATE INDEX IF NOT EXISTS "IX_CustomProgram_Subject" ON "CustomProgramEntity" ("SubjectKey", "Revision")""");
        c.Execute("""
            CREATE TABLE IF NOT EXISTS "SyncedSettingEntity" (
                "Id" varchar(36) primary key not null,
                "UserId" varchar,
                "CreatedAt" bigint,
                "UpdatedAt" bigint,
                "DeletedAt" bigint,
                "DeviceId" varchar,
                "SubjectKey" varchar,
                "Key" varchar,
                "Value" varchar
            )
            """);
        c.Execute("""CREATE INDEX IF NOT EXISTS "IX_SyncedSetting_Subject" ON "SyncedSettingEntity" ("SubjectKey")""");
        c.Execute("PRAGMA user_version = 4");
    }

    // Sync: per-table pull cursors per account, and a dirty marker on every synced row (existing rows start dirty).
    private static void V5(SQLiteConnection c)
    {
        c.Execute("""
            CREATE TABLE IF NOT EXISTS "sync_state" (
                "TableName" varchar not null,
                "UserId" varchar not null,
                "PullCursor" bigint,
                "PullLastId" varchar,
                primary key ("TableName", "UserId")
            )
            """);
        foreach (var table in new[]
        {
            "EnrollmentEntity", "CompletionEntity", "ScheduleShiftEntity", "BonusWorkoutEntity", "NoteEntity",
            "MetricEntryEntity", "ReminderEntity", "CustomProgramEntity", "SyncedSettingEntity",
        })
        {
            c.Execute($"""ALTER TABLE "{table}" ADD COLUMN "SyncPending" integer not null default 1""");
            c.Execute($"""CREATE INDEX IF NOT EXISTS "IX_{table}_SyncPending" ON "{table}" ("UserId", "SyncPending")""");
        }

        c.Execute("PRAGMA user_version = 5");
    }
}
