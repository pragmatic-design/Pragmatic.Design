using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Configuration;

namespace Pragmatic.Messaging.Tests.Unit;

public class IdempotencyPurgeServiceTests
{
    [Fact]
    public async Task ExecuteAsync_PurgesStorePeriodically()
    {
        var store = new RecordingIdempotencyStore();
        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyStore>(store);
        await using var sp = services.BuildServiceProvider();

        var options = new IdempotencyOptions
        {
            Retention = TimeSpan.FromDays(3),
            PurgeInterval = TimeSpan.FromMilliseconds(50),
        };
        using var service = new IdempotencyPurgeService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            options,
            NullLogger<IdempotencyPurgeService>.Instance);

        await service.StartAsync(CancellationToken.None);
        // Poll until at least one purge cycle ran.
        for (var i = 0; i < 100 && store.PurgeCalls == 0; i++)
            await Task.Delay(20);
        await service.StopAsync(CancellationToken.None);

        store.PurgeCalls.Should().BeGreaterThan(0);
        store.LastAge.Should().Be(TimeSpan.FromDays(3));
    }

    [Fact]
    public async Task ExecuteAsync_StoreFailure_DoesNotKillTheLoop()
    {
        var store = new RecordingIdempotencyStore { ThrowOnPurge = true };
        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyStore>(store);
        await using var sp = services.BuildServiceProvider();

        using var service = new IdempotencyPurgeService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            new IdempotencyOptions { PurgeInterval = TimeSpan.FromMilliseconds(50) },
            NullLogger<IdempotencyPurgeService>.Instance);

        await service.StartAsync(CancellationToken.None);
        // Two failing cycles prove the timer loop survives store exceptions.
        for (var i = 0; i < 100 && store.PurgeCalls < 2; i++)
            await Task.Delay(20);
        await service.StopAsync(CancellationToken.None);

        store.PurgeCalls.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void EnableIdempotency_RegistersStorePurgeServiceAndOptions()
    {
        var services = new ServiceCollection();
        var builder = new MessagingBuilder(services);

        builder.EnableIdempotency(o => o.Retention = TimeSpan.FromDays(1));

        services.Should().Contain(d => d.ServiceType == typeof(IIdempotencyStore));
        services.Should().Contain(d =>
            d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) &&
            d.ImplementationType == typeof(IdempotencyPurgeService));
        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IdempotencyOptions>().Retention.Should().Be(TimeSpan.FromDays(1));
    }

    private sealed class RecordingIdempotencyStore : IIdempotencyStore
    {
        private int _purgeCalls;
        public int PurgeCalls => _purgeCalls;
        public TimeSpan LastAge { get; private set; }
        public bool ThrowOnPurge { get; init; }

        public Task<bool> TryMarkAsProcessedAsync(string messageId, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<MessageClaim> TryClaimAsync(string messageId, TimeSpan lease, CancellationToken ct = default)
            => Task.FromResult(MessageClaim.Claimed);

        public Task MarkClaimCompletedAsync(string messageId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RemoveAsync(string messageId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task PurgeOlderThanAsync(TimeSpan age, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _purgeCalls);
            LastAge = age;
            return ThrowOnPurge ? Task.FromException(new InvalidOperationException("store down")) : Task.CompletedTask;
        }
    }
}
