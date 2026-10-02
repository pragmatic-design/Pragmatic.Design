using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events.EFCore.Outbox;
using Pragmatic.Events.Tests.Fixtures;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Events.Tests.EFCore;

/// <summary>
///     An event written inside a tenant carries that tenant, so the handler that receives it later
///     can see the same rows the request could.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>context.GetInfrastructure().GetService&lt;IServiceProvider&gt;()</c> is EF's
///         <b>internal</b> provider — asking it for <c>IServiceProvider</c> hands back the internal one
///         again, so every application service looked up through it answers null. An interceptor that
///         resolved the tenant that way would record "no tenant", the delivery would restore nothing,
///         and every query in the handler would read zero rows while the handler reported success.
///     </para>
///     <para>
///         ⚠️ <b>Nothing would fail, because both lookups have a fallback.</b> The tenant falls back to
///         null, which is a legitimate value for a single-tenant application, and the JSON options fall
///         back to the shared default, which serializes correctly. Both would silently stop being what
///         the application configured.
///     </para>
///     <para>
///         A test that constructs its <c>DbContext</c> by hand has no application provider and no tenant
///         to lose, so it cannot see this. This one registers the context the way an application does.
///     </para>
/// </remarks>
public sealed class TheOutboxRowCarriesTheTenantItWasWrittenInTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public TheOutboxRowCarriesTheTenantItWasWrittenInTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    /// <summary>An application-shaped container: AddDbContext, the interceptor, and a tenant.</summary>
    private ServiceProvider AnApplicationIn(string? tenantId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(new FixedTenant(tenantId));
        services.AddSingleton<EventOutboxInterceptor>();
        services.AddDbContext<OutboxTestDbContext>((sp, o) => o
            .UseSqlite(_connection)
            .AddInterceptors(sp.GetRequiredService<EventOutboxInterceptor>()));

        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>().Database.EnsureCreated();

        return provider;
    }

    private static EventOutboxEntry WriteAnEvent(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();

        var entity = new TestDbEntity { Name = "Initial" };
        context.Entities.Add(entity);
        context.SaveChanges();

        entity.ChangeName("Updated");
        context.SaveChanges();

        return context.Set<EventOutboxEntry>().AsNoTracking().Single();
    }

    /// <summary>The setpoint: written inside a tenant, the row says which one.</summary>
    [Fact]
    public void AnEventWrittenInATenant_IsWrittenDownWithIt()
    {
        using var provider = AnApplicationIn("acme");

        WriteAnEvent(provider).TenantId.Should().Be("acme",
            "delivery happens outside the request, so the tenant has to travel on the row");
    }

    /// <summary>
    ///     ⚠️ The control: no tenant is still no tenant.
    /// </summary>
    /// <remarks>
    ///     Without it, "the row carries the tenant" is satisfied by a row that carries something —
    ///     anything — and a single-tenant application would start writing a value nothing put there.
    ///     Null is the honest answer when nobody resolved a tenant, and it is what the delivery reads
    ///     as "no scope to restore".
    /// </remarks>
    [Fact]
    public void AnEventWrittenWithNoTenant_CarriesNone()
    {
        using var provider = AnApplicationIn(tenantId: null);

        WriteAnEvent(provider).TenantId.Should().BeNull();
    }

    public void Dispose() => _connection.Dispose();

    private sealed class FixedTenant(string? tenantId) : ITenantContext
    {
        public string? TenantId { get; } = tenantId;

        public string? TenantName => TenantId;

        public bool IsResolved => TenantId is { Length: > 0 };
    }
}
