#pragma warning disable CA2007 // xUnit manages SynchronizationContext

using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.EFCore;

namespace Pragmatic.Messaging.Tests.EFCore;

/// <summary>
///     <see cref="EfCoreScheduleHandleStore"/> on real SQLite (the store uses
///     ExecuteDeleteAsync, unsupported by the InMemory provider).
/// </summary>
public sealed class EfCoreScheduleHandleStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly EfCoreScheduleHandleStore _store;

    public EfCoreScheduleHandleStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<MessagingDbContext>(o => o.UseSqlite(_connection));
        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<MessagingDbContext>().Database.EnsureCreated();

        _store = new EfCoreScheduleHandleStore(_serviceProvider.GetRequiredService<IServiceScopeFactory>());
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task SaveGet_Roundtrip_ReturnsHandle()
    {
        var handle = new ScheduleHandle(Guid.NewGuid(), "orders", 42L);

        await _store.SaveAsync(handle);
        var retrieved = await _store.GetAsync(handle.ScheduleId);

        retrieved.Should().Be(handle);
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNull()
        => (await _store.GetAsync(Guid.NewGuid())).Should().BeNull();

    [Fact]
    public async Task Remove_ExistingHandle_GetReturnsNull()
    {
        var handle = new ScheduleHandle(Guid.NewGuid(), "orders", 7L);
        await _store.SaveAsync(handle);

        await _store.RemoveAsync(handle.ScheduleId);

        (await _store.GetAsync(handle.ScheduleId)).Should().BeNull();
    }

    [Fact]
    public async Task Remove_UnknownId_IsIdempotent()
    {
        var act = () => _store.RemoveAsync(Guid.NewGuid());
        await act.Should().NotThrowAsync();
    }
}
