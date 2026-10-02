using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy.Persistence;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.MultiTenancy.Tests;

/// <summary>
///     The one way to turn database-per-tenant on.
/// </summary>
/// <remarks>
///     <c>UseDbPerTenant</c> is the entry point, and nothing called it, no test ran it and no page
///     named it. The provisioner default is the part worth pinning: it is a no-op, so a host that
///     never registers a real one gets tenants whose database is silently never created.
/// </remarks>
public class MultiTenancyBuilderDbPerTenantExtensionsTests
{
    [Fact]
    public void UseDbPerTenant_RegistersTheOptionsAndAProvisioner()
    {
        var services = new ServiceCollection();

        services.AddPragmaticMultiTenancy(mt => mt.UseDbPerTenant(db =>
            db.ConnectionStringTemplate = "Server=localhost;Database=tenant_{0}"));

        services.Should().Contain(d => d.ServiceType == typeof(TenantDatabaseOptions));
        services.Should().Contain(d => d.ServiceType == typeof(ITenantDatabaseProvisioner),
            "the default is a no-op, and it has to be there for the resolution to succeed at all");
    }
}
