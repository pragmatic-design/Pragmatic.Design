using System.Threading.Channels;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration;
using Pragmatic.Configuration.Database;
using Pragmatic.Configuration.Database.Dialects;
using Pragmatic.Configuration.Database.Postgres;
using Xunit;

namespace Pragmatic.Configuration.Database.Tests.Postgres;

/// <summary>
///     End-to-end native watch over a real PostgreSQL container: writes propagate via LISTEN/NOTIFY (push),
///     including DELETEs. Polling is set to 10 minutes so anything observed within the test window can only
///     have arrived through the push path.
/// </summary>
public sealed class PostgresNativeWatchTests(PostgresConfigurationFixture fixture)
    : IClassFixture<PostgresConfigurationFixture>
{
    private ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbConnectionFactory>(
            new PostgresConfigurationFixture.NpgsqlConnectionFactory(fixture.ConnectionString!));
        services.AddDatabaseConfigurationStore(o =>
        {
            o.Provider = DatabaseProvider.PostgreSql;
            o.AutoCreateSchema = true;
            o.PollingInterval = TimeSpan.FromMinutes(10); // isolate push: only NOTIFY can deliver in-window
        });
        services.AddPostgresConfigurationWatch();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Push_UpsertAndDelete_DeliveredWithoutPolling()
    {
        if (fixture.ConnectionString is null)
            return; // Docker unavailable — skip.

        var sp = Build();
        await using (sp.ConfigureAwait(false))
        {
            var store = sp.GetRequiredService<IConfigurationStore>();

            // First call creates the schema + notify trigger.
            await store.GetAsync("warmup");

            using var watchCts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var changes = Channel.CreateUnbounded<ConfigurationChange>();
            var watch = Task.Run(async () =>
            {
                await foreach (var change in store.WatchAsync("Feature:*", watchCts.Token))
                    changes.Writer.TryWrite(change);
            });

            // Let the LISTEN connection establish before writing.
            await Task.Delay(TimeSpan.FromSeconds(2));

            await store.SetAsync("Feature:Flag", "on");
            var upsert = await ReadMatchAsync(changes.Reader, c => c.Key == "Feature:Flag" && c.NewValue == "on");
            upsert.Should().NotBeNull("an upsert must be pushed via NOTIFY well before the 10-minute poll");

            await store.DeleteAsync("Feature:Flag");
            var delete = await ReadMatchAsync(changes.Reader, c => c.Key == "Feature:Flag" && c.NewValue is null);
            delete.Should().NotBeNull("NOTIFY delivers deletes, which polling on updated_at can never observe");

            watchCts.Cancel();
            try { await watch; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Push_IgnoresKeysOutsideThePattern()
    {
        if (fixture.ConnectionString is null)
            return;

        var sp = Build();
        await using (sp.ConfigureAwait(false))
        {
            var store = sp.GetRequiredService<IConfigurationStore>();
            await store.GetAsync("warmup");

            using var watchCts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var changes = Channel.CreateUnbounded<ConfigurationChange>();
            var watch = Task.Run(async () =>
            {
                await foreach (var change in store.WatchAsync("Feature:*", watchCts.Token))
                    changes.Writer.TryWrite(change);
            });

            await Task.Delay(TimeSpan.FromSeconds(2));

            await store.SetAsync("Other:Key", "x");   // outside pattern — must not surface
            await store.SetAsync("Feature:Y", "y");    // inside pattern — the sentinel we wait for

            var seen = await ReadMatchAsync(changes.Reader, c => c.Key == "Feature:Y");
            seen.Should().NotBeNull();

            changes.Reader.TryRead(out var leftover).Should().BeFalse("keys outside the pattern must be filtered out");
            leftover.Should().BeNull();

            watchCts.Cancel();
            try { await watch; } catch (OperationCanceledException) { }
        }
    }

    private static async Task<ConfigurationChange?> ReadMatchAsync(
        ChannelReader<ConfigurationChange> reader, Func<ConfigurationChange, bool> predicate)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            while (await reader.WaitToReadAsync(cts.Token))
                while (reader.TryRead(out var change))
                    if (predicate(change))
                        return change;
        }
        catch (OperationCanceledException)
        {
            // Timed out.
        }

        return null;
    }
}
