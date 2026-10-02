using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Events;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.EFCore.Outbox;
using Pragmatic.Messaging.Entities;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     The delivery pump drains the outbox of a tenant that has a database of its own, not
///     only the shared one.
/// </summary>
/// <remarks>
///     <para>
///         An outbox row is written in the same transaction as the change it announces, so it lives in
///         the same database: with db-per-tenant that is the tenant's own. The pump is a background
///         service, so it has no request and no tenant, so its DbContext opens the connection it was
///         registered with — the shared database. Without a pass per tenant, every row in every
///         dedicated database would never be read, and the message would never leave the process.
///     </para>
///     <para>
///         ⚠️ Nothing would fail. There would be no error, no dead letter and no log: the pump reports
///         "0 pending" truthfully about the one database it looked at, while a tenant with a dedicated
///         database keeps a row with <c>ProcessedAt</c> null.
///     </para>
///     <para>
///         The routing here is the smallest thing shaped like <c>TenantConnectionInterceptor</c>: the
///         context factory picks the connection from the ambient tenant. SQLite, so the test is
///         hermetic; what is measured is which databases the pump visits, which is not
///         provider-specific.
///     </para>
/// </remarks>
#pragma warning disable CA2007
public sealed class TheOutboxOfATenantWithItsOwnDatabaseTests : IDisposable
{
    private const string Northwind = "northwind";

    /// <summary>The database every tenant without one of its own writes to.</summary>
    private readonly SqliteConnection _shared = new("DataSource=shared-outbox;Mode=Memory;Cache=Shared");

    /// <summary>Northwind's own. Nothing but Northwind's scope ever points at it.</summary>
    private readonly SqliteConnection _dedicated = new("DataSource=northwind-outbox;Mode=Memory;Cache=Shared");

    public TheOutboxOfATenantWithItsOwnDatabaseTests()
    {
        _shared.Open();
        _dedicated.Open();
    }

    public void Dispose()
    {
        _shared.Dispose();
        _dedicated.Dispose();
    }

    [Fact]
    public async Task ThePump_DeliversTheRowsOfEveryTenantsOwnDatabase()
    {
        var bus = new CollectingBus();
        await using var application = AnApplicationWithTwoDatabases(bus);

        await CreateSchemaAsync(application, tenant: null);
        await CreateSchemaAsync(application, tenant: Northwind);

        await PutARowInAsync(application, tenant: null);
        await PutARowInAsync(application, tenant: Northwind);

        var pump = application.GetServices<IHostedService>().OfType<OutboxDeliveryService>().Single();
        await pump.StartAsync(CancellationToken.None);

        try
        {
            await WaitUntilAsync(() => bus.Published.Count == 2, TimeSpan.FromSeconds(15));
        }
        finally
        {
            await pump.StopAsync(CancellationToken.None);
        }

        bus.Published.Should().HaveCount(2,
            "the row in Northwind's own database is a message that has to leave the process too");
        bus.Tenants.Should().Contain(Northwind);

        (await PendingInAsync(application, tenant: Northwind)).Should().Be(0,
            "and it was marked processed there, in the database it was read from");
    }

    /// <summary>
    ///     One tenant whose database cannot be opened does not stop the others being delivered.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It is the failure mode a per-tenant sweep invites: a register can name a database that is
    ///     gone, unreachable or never made — an onboarding that failed half way leaves exactly that. A
    ///     sweep that let the exception out of the batch would stop delivery for <b>every</b> tenant, for
    ///     ever, with one line in a log.
    /// </remarks>
    [Fact]
    public async Task ATenantWhoseDatabaseCannotBeOpened_DoesNotStopTheOthers()
    {
        var bus = new CollectingBus();
        await using var application = AnApplicationWithTwoDatabases(bus, unreachableTenant: true);

        await CreateSchemaAsync(application, tenant: null);
        await PutARowInAsync(application, tenant: null);

        var pump = application.GetServices<IHostedService>().OfType<OutboxDeliveryService>().Single();
        await pump.StartAsync(CancellationToken.None);

        try
        {
            await WaitUntilAsync(() => bus.Published.Count == 1, TimeSpan.FromSeconds(15));
        }
        finally
        {
            await pump.StopAsync(CancellationToken.None);
        }

        bus.Published.Should().ContainSingle(
            "the shared database's row is delivered although another tenant's database is not there");
    }

    /// <summary>
    ///     The control: with no tenant holding a database of its own, the pump makes exactly one pass.
    /// </summary>
    /// <remarks>
    ///     Without it "drains every database" would be satisfied by draining the shared one N+1 times —
    ///     which delivers the same row repeatedly and is how at-least-once turns into several-times.
    /// </remarks>
    [Fact]
    public async Task WhenNoTenantHasItsOwnDatabase_TheSharedRowIsDeliveredOnce()
    {
        var bus = new CollectingBus();
        await using var application = AnApplicationWithTwoDatabases(bus, dedicated: false);

        await CreateSchemaAsync(application, tenant: null);
        await PutARowInAsync(application, tenant: null);

        var pump = application.GetServices<IHostedService>().OfType<OutboxDeliveryService>().Single();
        await pump.StartAsync(CancellationToken.None);

        try
        {
            await WaitUntilAsync(() => bus.Published.Count >= 1, TimeSpan.FromSeconds(15));
            // Two more polling intervals: a second pass over the same database would publish it again.
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
        finally
        {
            await pump.StopAsync(CancellationToken.None);
        }

        bus.Published.Should().ContainSingle("the one row was delivered once");
    }

    private ServiceProvider AnApplicationWithTwoDatabases(
        CollectingBus bus, bool dedicated = true, bool unreachableTenant = false)
    {
        var tenants = new OneOrganisation(new TenantInfo
        {
            TenantId = Northwind,
            TenantName = Northwind,
            // What makes this tenant's rows live elsewhere. The value is not read here — the routing
            // below is — but it is what the pump looks for when it decides where to go.
            ConnectionString = dedicated ? _dedicated.ConnectionString : null,
            State = TenantState.Active,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<ITenantStore>(tenants);
        services.AddSingleton<ITenantContext>(new TenantScope());
        services.AddSingleton<IMessageBus>(bus);
        services.AddSingleton<IMessageTypeRegistry>(new OneMessage());
        services.AddSingleton(Options.Create(new MessagingOptions { PollingIntervalSeconds = 1 }));

        // Routing, as db-per-tenant does it: the tenant of the current scope decides the connection.
        services.AddDbContext<ThingsDbContext>((sp, options) =>
        {
            var tenant = sp.GetRequiredService<ITenantContext>().TenantId;

            // A database that is not there: what a register names after an onboarding that failed half
            // way, or after somebody dropped it.
            if (tenant == Northwind && unreachableTenant)
                options.UseSqlite("DataSource=/no/such/place/missing.db");
            else
                options.UseSqlite(tenant == Northwind && dedicated ? _dedicated : _shared);
        });

        services.AddMessagingOutbox<ThingsDbContext>("Things");

        return services.BuildServiceProvider();
    }

    private static async Task CreateSchemaAsync(IServiceProvider application, string? tenant)
    {
        using var tenantScope = tenant is null ? null : TenantScope.BeginScope(tenant);
        using var scope = application.CreateScope();

        await scope.ServiceProvider.GetRequiredService<ThingsDbContext>().Database.EnsureCreatedAsync();
    }

    /// <summary>One pending outbox row, in whichever database this tenant's scope points at.</summary>
    private static async Task PutARowInAsync(IServiceProvider application, string? tenant)
    {
        using var tenantScope = tenant is null ? null : TenantScope.BeginScope(tenant);
        using var scope = application.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ThingsDbContext>();

        context.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageType = typeof(SomethingHappened).FullName!,
            Payload = JsonSerializer.Serialize(new SomethingHappened(DateTimeOffset.UtcNow)),
            CreatedAt = DateTimeOffset.UtcNow,
            TenantId = tenant
        });

        await context.SaveChangesAsync();
    }

    private static async Task<int> PendingInAsync(IServiceProvider application, string? tenant)
    {
        using var tenantScope = tenant is null ? null : TenantScope.BeginScope(tenant);
        using var scope = application.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ThingsDbContext>()
            .Set<OutboxMessage>().CountAsync(m => m.ProcessedAt == null);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(50);
        }

        throw new TimeoutException($"Condition was still false after {timeout.TotalSeconds:0.#}s.");
    }

    private sealed class ThingsDbContext(DbContextOptions<ThingsDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.AddMessagingOutbox();
    }

    private sealed record SomethingHappened(DateTimeOffset OccurredAt) : IDomainEvent;

    /// <summary>
    ///     A register with one organisation in it. Written here rather than reusing
    ///     <c>InMemoryTenantStore</c>: that lives in <c>Pragmatic.MultiTenancy</c>, which this suite does
    ///     not reference, and the pump only asks the contract in <c>Pragmatic.Abstractions</c>.
    /// </summary>
    private sealed class OneOrganisation(TenantInfo tenant) : ITenantStore
    {
        public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(tenantId == tenant.TenantId ? tenant : null);

        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>([tenant]);

        public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>(
                tenant.State == TenantState.Active ? [tenant] : []);

        public Task<TenantInfo> CreateAsync(TenantInfo newTenant, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> UpdateAsync(TenantInfo changed, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class OneMessage : IMessageTypeRegistry
    {
        public object? Deserialize(string fullyQualifiedTypeName, string json)
            => fullyQualifiedTypeName == typeof(SomethingHappened).FullName
                ? JsonSerializer.Deserialize<SomethingHappened>(json)
                : null;
    }

    private sealed class CollectingBus : IMessageBus
    {
        public List<object> Published { get; } = [];

        public List<string?> Tenants { get; } = [];

        public Task PublishAsync(
            object message, Type messageType, MessageContext context, CancellationToken ct = default)
        {
            lock (Published)
            {
                Published.Add(message);
                Tenants.Add(context.TenantId);
            }

            return Task.CompletedTask;
        }

        public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : notnull
            => throw new NotSupportedException();

        public Task PublishAsync<T>(T message, MessageContext context, CancellationToken ct = default)
            where T : notnull => throw new NotSupportedException();

        public Task DispatchAsync(object message, MessageContext context, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SendAsync<T>(T message, CancellationToken ct = default) where T : notnull
            => throw new NotSupportedException();

        public Task SendAsync<T>(T message, MessageContext context, CancellationToken ct = default)
            where T : notnull => throw new NotSupportedException();

        public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
            where TRequest : notnull where TResponse : notnull => throw new NotSupportedException();
    }
}
