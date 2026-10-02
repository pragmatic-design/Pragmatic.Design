using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Events.EFCore.Outbox;
using Pragmatic.Events.Tests.Fixtures;
using Pragmatic.MultiTenancy;
using Pragmatic.Serialization;

namespace Pragmatic.Events.Tests.EFCore;

/// <summary>
///     The outbox must restore the tenant an entry was written under before dispatching it, so a
///     handler's tenant-scoped queries read the right rows instead of the worker's null tenant.
/// </summary>
/// <remarks>
///     The container here mirrors production deliberately: <see cref="ITenantContext" /> binds to a
///     read-only composite that does <b>not</b> implement <see cref="IMutableTenantContext" />, and the
///     mutable interface is a separate registration over the same object. Registering one object under
///     both interfaces would let a <c>GetService&lt;ITenantContext&gt;() as IMutableTenantContext</c>
///     cast succeed, and this suite would pass against the very bug it exists to catch.
/// </remarks>
public sealed class EventOutboxTenantRestoreTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = PragmaticJsonOptions.Default.Build();

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    /// <summary>Tenant seen on the delivery scope at each dispatch, in delivery order.</summary>
    private readonly List<string?> _observed = [];

    private int _seeded;

    public EventOutboxTenantRestoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<OutboxTestDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<IEventOutboxTypeResolver>(new EventOutboxTypeResolver([typeof(TestDomainEvent)]));

        services.AddScoped<TestMutableTenantContext>();
        services.AddScoped<ITenantContext>(sp => new ReadOnlyTenantContext(sp.GetRequiredService<TestMutableTenantContext>()));
        services.AddScoped<IMutableTenantContext>(sp => sp.GetRequiredService<TestMutableTenantContext>());

        // Scoped, so it reads the delivery scope's tenant context — the same instance an EF tenant
        // query filter would resolve inside a handler.
        services.AddScoped<IDomainEventDispatcher>(sp =>
            new TenantRecordingDispatcher(_observed, sp.GetRequiredService<ITenantContext>()));

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task DeliverBatch_EntryWithTenant_DispatchesUnderThatTenant()
    {
        SeedEntry(new TestDomainEvent("scoped"), tenantId: "tenant-a");

        await CreateDrainer().DrainOnceAsync(CancellationToken.None);

        _observed.Should().Equal("tenant-a");
    }

    [Fact]
    public async Task DeliverBatch_EntriesForDifferentTenants_EachDispatchesUnderItsOwn()
    {
        // The restore is per entry, not per batch: the second entry must not inherit the first's tenant.
        SeedEntry(new TestDomainEvent("first"), tenantId: "tenant-a");
        SeedEntry(new TestDomainEvent("second"), tenantId: "tenant-b");

        await CreateDrainer().DrainOnceAsync(CancellationToken.None);

        _observed.Should().Equal("tenant-a", "tenant-b");
    }

    [Fact]
    public async Task DeliverBatch_TenantedThenUntenantedEntry_DoesNotLeakTheFirstTenant()
    {
        // SetTenant's disposable must put the previous value back: an entry with no tenant that follows
        // a tenanted one has to be dispatched with no tenant, not with the previous entry's.
        SeedEntry(new TestDomainEvent("tenanted"), tenantId: "tenant-a");
        SeedEntry(new TestDomainEvent("untenanted"), tenantId: null);

        await CreateDrainer().DrainOnceAsync(CancellationToken.None);

        _observed.Should().Equal("tenant-a", null);
    }

    private EventOutboxDrainer<OutboxTestDbContext> CreateDrainer()
        => new(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new EventOutboxOptions(),
            NullLogger<EventOutboxDrainer<OutboxTestDbContext>>.Instance);

    private void SeedEntry(IDomainEvent domainEvent, string? tenantId)
    {
        using var scope = _provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
        ctx.Add(new EventOutboxEntry
        {
            Id = Guid.NewGuid(),
            EventType = domainEvent.GetType().AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(domainEvent, JsonOptions.GetTypeInfo(domainEvent.GetType())),
            OccurredAt = domainEvent.OccurredAt,
            // Delivery orders by CreatedAt: keep the seeds apart so the sequence assertions are stable.
            CreatedAt = DateTimeOffset.UtcNow.AddMilliseconds(_seeded++),
            TenantId = tenantId,
        });
        ctx.SaveChanges();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    /// <summary>Records the tenant visible on the delivery scope at the moment of dispatch.</summary>
    private sealed class TenantRecordingDispatcher(List<string?> observed, ITenantContext tenant)
        : IDomainEventDispatcher
    {
        public Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : IDomainEvent
            => DispatchAsync([@event], ct);

        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
        {
            foreach (var _ in events)
                observed.Add(tenant.TenantId);

            return Task.CompletedTask;
        }
    }

    /// <summary>Mutable context, as the MultiTenancy package registers it.</summary>
    private sealed class TestMutableTenantContext : IMutableTenantContext
    {
        public string? TenantId { get; private set; }

        public string? TenantName { get; private set; }

        public bool IsResolved => !string.IsNullOrEmpty(TenantId);

        public IDisposable SetTenant(string? tenantId, string? tenantName = null)
        {
            var (previousId, previousName) = (TenantId, TenantName);
            TenantId = tenantId;
            TenantName = tenantName;
            return new Restore(this, previousId, previousName);
        }

        private sealed class Restore(TestMutableTenantContext ctx, string? id, string? name) : IDisposable
        {
            public void Dispose()
            {
                ctx.TenantId = id;
                ctx.TenantName = name;
            }
        }
    }

    /// <summary>
    ///     The stand-in for <c>AmbientTenantContext</c>, and it mirrors it exactly: the request tenant
    ///     when one is resolved, the ambient <see cref="TenantScope" /> otherwise.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read-only, as <c>AmbientTenantContext</c> is registered for <see cref="ITenantContext" />:
    ///         not implementing <see cref="IMutableTenantContext" /> is the whole point of this type.
    ///     </para>
    ///     ⚠️ The fallback is not a concession: this class exists to mirror production, as the remark on
    ///     the fixture says, and production restores a background tenant through <c>TenantScope</c>.
    ///     Without the fallback the fixture would be asserting a mechanism the framework does not use —
    ///     green over a worker that reads no tenant at all.
    /// </remarks>
    private sealed class ReadOnlyTenantContext(TestMutableTenantContext inner) : ITenantContext
    {
        private static readonly TenantScope Ambient = new();

        public string? TenantId => inner.IsResolved ? inner.TenantId : Ambient.TenantId;

        public string? TenantName => inner.IsResolved ? inner.TenantName : Ambient.TenantName;

        public bool IsResolved => inner.IsResolved || Ambient.IsResolved;
    }
}
