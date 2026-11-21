namespace ShowUp.Core.Storage;

public interface IReminderRepository
{
    Task<IReadOnlyList<ReminderEntity>> ListAsync();

    Task<ReminderEntity> AddAsync(int minutesOfDay, int daysMask, bool enabled = true);

    Task UpdateAsync(Guid id, int minutesOfDay, int daysMask);

    Task SetEnabledAsync(Guid id, bool enabled);

    Task DeleteAsync(Guid id);
}
