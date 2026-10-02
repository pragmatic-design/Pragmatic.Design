using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events;
using Pragmatic.Messaging.Entities;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Tests.EFCore;

/// <summary>
///     An outbox row carries the tenant of the change it announces.
/// </summary>
/// <remarks>
///     <para>
///         The interceptor is a singleton, so it reads the tenant per call from the application's service
///         provider. <c>context.GetInfrastructure().GetService&lt;IServiceProvider&gt;()</c> is the wrong
///         one: it is EF's <b>internal</b> provider, where no application service is registered, so
///         <c>ITenantContext</c>, <c>ICurrentUser</c> and <c>PragmaticJsonOptions</c> would all resolve to
///         null and every outbox row would be written with no tenant, no user and the default JSON options.
///     </para>
///     <para>
///         ⚠️ <b>Why this test registers the context through a container.</b> <c>DbContextOptions</c>
///         built by hand — <c>new DbContextOptionsBuilder&lt;T&gt;().UseInMemoryDatabase(…)</c> — have no
///         application provider at all, so "the provider is the wrong one" and "there is no provider" are
///         the same thing there. Registering the context the way an application does, through
///         <c>AddDbContext</c> in a container, is the only shape in which the two differ.
///     </para>
///     <para>
///         In a deployment the failure shows downstream: the consuming service writes its row with an
///         <b>empty</b> tenant — invisible afterwards to that service's own API, whose query filter is
///         fail-closed. The message crosses and the organisation does not.
///     </para>
/// </remarks>
public class TheOutboxRowCarriesTheTenantTests
{
    private sealed record SomethingHappened(DateTimeOffset OccurredAt) : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
    }

    private sealed class Thing : IHasDomainEvents
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        private readonly List<IDomainEvent> _events = [];

        public IReadOnlyList<IDomainEvent> DomainEvents => _events;

        public void ClearDomainEvents() => _events.Clear();

        public void RaiseEvent(IDomainEvent @event) => _events.Add(@event);
    }

    private sealed class ThingsDbContext(DbContextOptions<ThingsDbContext> options) : DbContext(options)
    {
        public DbSet<Thing> Things => Set<Thing>();

        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Thing>().HasKey(e => e.Id);
            new OutboxEntityTypeConfiguration().Configure(modelBuilder.Entity<OutboxMessage>());
        }
    }

    /// <summary>A tenant that is simply there, as an application's resolved one is.</summary>
    private sealed class TheTenant(string id) : ITenantContext
    {
        public string? TenantId => id;

        public string? TenantName => id;

        public bool IsResolved => true;
    }

    private static ServiceProvider AnApplication(ITenantContext tenant)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tenant);
        services.AddSingleton<OutboxInterceptor>();
        services.AddDbContext<ThingsDbContext>((sp, options) => options
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(sp.GetRequiredService<OutboxInterceptor>()));

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task TheRow_CarriesTheResolvedTenant()
    {
        await using var application = AnApplication(new TheTenant("acme"));
        using var scope = application.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ThingsDbContext>();

        var thing = new Thing();
        thing.RaiseEvent(new SomethingHappened(DateTimeOffset.UtcNow));
        context.Things.Add(thing);
        await context.SaveChangesAsync();

        var row = await context.OutboxMessages.SingleAsync();

        row.TenantId.Should().Be("acme",
            "the consuming service has no other way to know whose change this was");
    }

    /// <summary>
    ///     The control: with no tenant resolved the column stays empty, rather than being filled with
    ///     something.
    /// </summary>
    [Fact]
    public async Task WithNoTenantResolved_TheRowHasNone()
    {
        var services = new ServiceCollection();
        services.AddSingleton<OutboxInterceptor>();
        services.AddDbContext<ThingsDbContext>((sp, options) => options
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(sp.GetRequiredService<OutboxInterceptor>()));

        await using var application = services.BuildServiceProvider();
        using var scope = application.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ThingsDbContext>();

        var thing = new Thing();
        thing.RaiseEvent(new SomethingHappened(DateTimeOffset.UtcNow));
        context.Things.Add(thing);
        await context.SaveChangesAsync();

        (await context.OutboxMessages.SingleAsync()).TenantId.Should().BeNull(
            "a single-tenant application publishes rows that belong to nobody, and that is correct");
    }
}
