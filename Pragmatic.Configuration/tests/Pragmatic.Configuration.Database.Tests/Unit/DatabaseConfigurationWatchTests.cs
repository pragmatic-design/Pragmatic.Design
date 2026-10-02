using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration.Database.Dialects;

namespace Pragmatic.Configuration.Database.Tests.Unit;

/// <summary>
///     SQLite change polling must not skip a configuration
///     change written in the same wall-clock second as a sub-second watcher cursor.
/// </summary>
public sealed class DatabaseConfigurationWatchTests : IDisposable
{
    private readonly SqliteConnectionFactory _factory;
    private readonly IConfigurationStore _store;
    private readonly ServiceProvider _sp;

    public DatabaseConfigurationWatchTests()
    {
        _factory = new SqliteConnectionFactory();
        var services = new ServiceCollection();

        services.AddSingleton<IDbConnectionFactory>(_factory);
        services.AddDatabaseConfigurationStore(opts =>
        {
            opts.Provider = DatabaseProvider.Sqlite;
            opts.AutoCreateSchema = true;
            opts.AuditUser = "test-user";
            // Fast polling so the test observes changes quickly.
            opts.PollingInterval = TimeSpan.FromMilliseconds(50);
        });

        services.AddLogging();
        _sp = services.BuildServiceProvider();
        _store = _sp.GetRequiredService<IConfigurationStore>();
    }

    [Fact]
    public async Task WatchAsync_TwoChangesInSameSecond_ObservesBoth()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var observed = new List<string>();

        // Start watching first so the cursor is a sub-second DateTimeOffset.UtcNow.
        var watchTask = Task.Run(async () =>
        {
            await foreach (var change in _store.WatchAsync("App:*", cts.Token).ConfigureAwait(false))
            {
                lock (observed)
                    observed.Add(change.Key);

                bool done;
                lock (observed)
                    done = observed.Contains("App:First") && observed.Contains("App:Second");

                if (done)
                    break;
            }
        }, cts.Token);

        // Let the watcher establish its cursor.
        await Task.Delay(120, cts.Token);

        // Two writes inside the same wall-clock second. With second-precision storage the second
        // write (after the sub-second cursor) would be permanently skipped.
        await _store.SetAsync("App:First", "1");
        await _store.SetAsync("App:Second", "2");

        try
        {
            await watchTask;
        }
        catch (OperationCanceledException)
        {
            // Fall through to the assertion which reports what was actually observed.
        }

        lock (observed)
        {
            observed.Should().Contain("App:First");
            observed.Should().Contain("App:Second");
        }
    }

    public void Dispose()
    {
        _sp.Dispose();
        _factory.Dispose();
    }
}
