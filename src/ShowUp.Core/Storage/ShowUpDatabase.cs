using SQLite;

namespace ShowUp.Core.Storage;

public sealed class ShowUpDatabase(string databasePath, Func<Task>? prepareStorage = null) : IAsyncDisposable
{
    private static readonly Lazy<bool> _sqliteInit = new(() =>
    {
        SQLitePCL.Batteries_V2.Init();
        return true;
    });

    private readonly SemaphoreSlim _gate = new(1, 1);
    private SQLiteAsyncConnection? _connection;

    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _gate.WaitAsync();
        try
        {
            if (_connection is null)
            {
                _ = _sqliteInit.Value;
                if (prepareStorage is not null)
                {
                    await prepareStorage();
                }

                SQLiteAsyncConnection connection = new(databasePath);
                await Migrations.ApplyAsync(connection);
                _connection = connection;
            }

            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection = null;
        }
    }
}
