using SQLite;

namespace ShowUp.Core.Storage;

// Each version is frozen DDL, never the live entity classes, so later versions can ALTER safely.
public static class Migrations
{
    public static async Task ApplyAsync(SQLiteAsyncConnection connection)
    {
        int version = await connection.ExecuteScalarAsync<int>("PRAGMA user_version");

        if (version < 1)
        {
            await connection.RunInTransactionAsync(V1);
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
}
