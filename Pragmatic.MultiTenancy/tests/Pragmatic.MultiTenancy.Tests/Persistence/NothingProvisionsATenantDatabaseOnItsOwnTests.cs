using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Migrations.Tenant;
using Pragmatic.MultiTenancy.Persistence;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.MultiTenancy.Tests.Persistence;

/// <summary>
///     What <c>AddDbPerTenant</c> puts in the container, and what it deliberately does not do.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>There is no "provision on first access".</b> <c>TenantDatabaseOptions</c> has no option
///         that creates the database and runs migrations when a tenant with a dedicated connection string
///         is first accessed, and the first request for such a tenant fails with the driver's
///         <c>3D000: database does not exist</c>. A property that read as that feature flag and did
///         nothing would be worse than its absence.
///     </para>
///     <para>
///         The cases pin the claims made about this registration rather than leaving them to reading,
///         because a doubt settled by reading is a doubt that comes back.
///     </para>
/// </remarks>
public class NothingProvisionsATenantDatabaseOnItsOwnTests
{
    private static IServiceCollection Registrations()
        => new ServiceCollection()
            .AddLogging()
            .AddDbPerTenant(o =>
            {
                o.DefaultConnectionString = "Host=shared;Database=app";
                o.ConnectionStringTemplate = "Host=shared;Database=tenant_{0}";
            });

    private static ServiceProvider Container() => Registrations().BuildServiceProvider();

    /// <summary>
    ///     ⚠️ The provisioner an application gets without asking creates <b>nothing</b>.
    /// </summary>
    /// <remarks>
    ///     The right default for a deployment whose control plane owns the database server, and the
    ///     wrong one for a service that onboards its own customers — which is why it is a choice
    ///     (<c>UseAutoProvision&lt;T&gt;()</c>) and not a setting. A tenant whose row names a database
    ///     nobody created is served by this, and what it gets is the driver's error on first query.
    /// </remarks>
    [Fact]
    public void TheDefaultProvisioner_CreatesNothing()
    {
        using var container = Container();

        container.GetRequiredService<ITenantDatabaseProvisioner>()
            .Should().BeOfType<NoOpTenantProvisioner>();
    }

    /// <summary>
    ///     The per-tenant migration orchestrator <b>is</b> registered — the claim its summary makes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>AddDbPerTenant</c>'s summary says it wires "per-tenant migration orchestration", and
    ///         the issue doubted it on the strength of the option beside it being inert. It is true.
    ///     </para>
    ///     <para>
    ///         ⚠️ Asserted on the <b>registration</b> and not by resolving it, because resolving it needs
    ///         an <c>ITenantStore</c> and an <c>IMigrationRunner</c> that the application supplies —
    ///         measured here as the exact resolution failure when this case first tried. Which is the
    ///         precise form of the claim: the wiring is here, the collaborators are the application's.
    ///     </para>
    ///     <para>
    ///         ⚠️ And what is registered is not what is <em>run</em>: nothing migrates on a request. The
    ///         generated host entry point calls <c>MigrateAllTenantsAsync</c> at startup, and an
    ///         application calls it whenever it decides to.
    ///     </para>
    /// </remarks>
    [Fact]
    public void ThePerTenantMigrationOrchestrator_IsRegistered()
    {
        var services = Registrations();

        services.Any(d => d.ServiceType == typeof(ITenantMigrationOrchestrator))
            .Should().BeTrue("AddDbPerTenant's summary says it wires per-tenant migration orchestration");

        using var container = services.BuildServiceProvider();
        container.GetService<TenantMigrationOptions>().Should().NotBeNull(
            "its options ARE resolvable on their own, so a host can override them before that call");
    }

    /// <summary>
    ///     ⚠️ The configured options instance is the one the container hands out.
    /// </summary>
    /// <remarks>
    ///     The control on the two cases above — they resolve services built from these options, and an
    ///     empty template resolved by mistake is a known defect (injecting
    ///     <c>IOptions&lt;TenantDatabaseOptions&gt;</c> yields a brand new, empty one). Asserted on the
    ///     template because that is the member whose emptiness is invisible until a connection string
    ///     comes out with no database in it.
    /// </remarks>
    [Fact]
    public void TheConfiguredOptions_AreWhatResolves()
    {
        using var container = Container();

        container.GetRequiredService<TenantDatabaseOptions>()
            .ConnectionStringTemplate.Should().Be("Host=shared;Database=tenant_{0}");
    }

    /// <summary>
    ///     Choosing a provisioner replaces the default rather than adding beside it.
    /// </summary>
    /// <remarks>
    ///     Two registered provisioners would make "which one creates the database" depend on resolution
    ///     order. <c>UseAutoProvision&lt;T&gt;()</c> removes every existing registration first.
    /// </remarks>
    [Fact]
    public void ChoosingAProvisioner_LeavesExactlyOne()
    {
        var services = Registrations();
        services.UseAutoProvision<PostgresTenantProvisioner>();

        using var container = services.BuildServiceProvider();

        container.GetServices<ITenantDatabaseProvisioner>()
            .Should().ContainSingle().Which.Should().BeOfType<PostgresTenantProvisioner>();
    }
}
