namespace ShowUp.Core.Storage;

public sealed class SqliteReminderRepository(ShowUpDatabase db, TimeProvider time, IUserScope user, IDeviceIdProvider device)
    : SqliteRepositoryBase(db, time, user, device), IReminderRepository
{
    public async Task<IReadOnlyList<ReminderEntity>> ListAsync()
    {
        var connection = await ConnectAsync();
        var rows = await QueryAsync<ReminderEntity>(connection, _ => true);
        return rows.OrderBy(r => r.MinutesOfDay).ThenBy(r => r.CreatedAt).ToList();
    }

    public async Task<ReminderEntity> AddAsync(int minutesOfDay, int daysMask, bool enabled = true)
    {
        var connection = await ConnectAsync();
        var reminder = Stamp(new ReminderEntity { MinutesOfDay = minutesOfDay, DaysMask = daysMask, Enabled = enabled });
        await connection.InsertAsync(reminder);
        return reminder;
    }

    public Task UpdateAsync(Guid id, int minutesOfDay, int daysMask) => ModifyAsync(id, r =>
    {
        r.MinutesOfDay = minutesOfDay;
        r.DaysMask = daysMask;
    });

    public Task SetEnabledAsync(Guid id, bool enabled) => ModifyAsync(id, r => r.Enabled = enabled);

    public async Task DeleteAsync(Guid id)
    {
        var connection = await ConnectAsync();
        await connection.RunInTransactionAsync(c => Tombstone(c, Query<ReminderEntity>(c, r => r.Id == id)));
    }

    private async Task ModifyAsync(Guid id, Action<ReminderEntity> change)
    {
        var connection = await ConnectAsync();
        await connection.RunInTransactionAsync(c =>
        {
            foreach (var reminder in Query<ReminderEntity>(c, r => r.Id == id))
            {
                change(reminder);
                Touch(c, reminder);
            }
        });
    }
}
