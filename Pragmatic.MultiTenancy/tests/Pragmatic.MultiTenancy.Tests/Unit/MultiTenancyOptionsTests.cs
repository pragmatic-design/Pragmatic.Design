using Pragmatic.Testing.Assertions;

namespace Pragmatic.MultiTenancy.Tests.Unit;

public class MultiTenancyOptionsTests
{
    [Fact]
    public void Default_DefaultTenantId_IsDefault()
    {
        var options = new MultiTenancyOptions();

        options.DefaultTenantId.Should().Be("default");
    }

    [Fact]
    public void Default_TenantHeaderName_IsXTenantId()
    {
        var options = new MultiTenancyOptions();

        options.TenantHeaderName.Should().Be("X-Tenant-Id");
    }

    [Fact]
    public void Default_TenantClaimType_IsTenantId()
    {
        var options = new MultiTenancyOptions();

        options.TenantClaimType.Should().Be("tenant_id");
    }

    [Fact]
    public void Default_TenantRouteParameter_IsTenantId()
    {
        var options = new MultiTenancyOptions();

        options.TenantRouteParameter.Should().Be("tenantId");
    }

    [Fact]
    public void Default_RequireTenant_IsTrue()
    {
        var options = new MultiTenancyOptions();

        // Secure default: an unresolved tenant fails closed (and tenant queries return no rows).
        options.RequireTenant.Should().BeTrue();
    }

    [Fact]
    public void AllProperties_AreMutable()
    {
        var options = new MultiTenancyOptions
        {
            DefaultTenantId = "root",
            TenantHeaderName = "X-Custom-Tenant",
            TenantClaimType = "tid",
            TenantRouteParameter = "tenant",
            RequireTenant = true
        };

        options.DefaultTenantId.Should().Be("root");
        options.TenantHeaderName.Should().Be("X-Custom-Tenant");
        options.TenantClaimType.Should().Be("tid");
        options.TenantRouteParameter.Should().Be("tenant");
        options.RequireTenant.Should().BeTrue();
    }
}
