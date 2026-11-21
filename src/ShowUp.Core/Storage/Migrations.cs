using SQLite;

namespace ShowUp.Core.Storage;

public static class Migrations
{
    public static async Task ApplyAsync(SQLiteAsyncConnection connection)
    {
        int version = await connection.ExecuteScalarAsync<int>("PRAGMA user_version");

        if (version < 1)
        {
            await connection.CreateTableAsync<EnrollmentEntity>();
            await connection.CreateTableAsync<CompletionEntity>();
            await connection.CreateTableAsync<ScheduleShiftEntity>();
            await connection.CreateTableAsync<BonusWorkoutEntity>();
            await connection.ExecuteAsync("PRAGMA user_version = 1");
        }
    }
}
