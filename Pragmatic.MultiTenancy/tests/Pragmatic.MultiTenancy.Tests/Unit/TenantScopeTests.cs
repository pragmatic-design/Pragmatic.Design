using Pragmatic.Testing.Assertions;

namespace Pragmatic.MultiTenancy.Tests.Unit;

[Collection(TenantScopeCollection.Name)]
public class TenantScopeTests
{
    [Fact]
    public void Default_IsNotResolved()
    {
        var scope = new TenantScope();

        scope.TenantId.Should().BeNull();
        scope.TenantName.Should().BeNull();
        scope.IsResolved.Should().BeFalse();
    }

    [Fact]
    public void BeginScope_SetsTenantId()
    {
        var tenantScope = new TenantScope();

        using var _ = TenantScope.BeginScope("tenant-42", "Test Tenant");

        tenantScope.TenantId.Should().Be("tenant-42");
        tenantScope.TenantName.Should().Be("Test Tenant");
        tenantScope.IsResolved.Should().BeTrue();
    }

    [Fact]
    public void Dispose_RestoresPreviousScope()
    {
        var tenantScope = new TenantScope();

        using (TenantScope.BeginScope("tenant-1"))
        {
            tenantScope.TenantId.Should().Be("tenant-1");
        }

        tenantScope.TenantId.Should().BeNull();
        tenantScope.IsResolved.Should().BeFalse();
    }

    [Fact]
    public void NestedScopes_RestoreCorrectly()
    {
        var tenantScope = new TenantScope();

        using (TenantScope.BeginScope("outer"))
        {
            tenantScope.TenantId.Should().Be("outer");

            using (TenantScope.BeginScope("inner"))
            {
                tenantScope.TenantId.Should().Be("inner");
            }

            tenantScope.TenantId.Should().Be("outer");
        }

        tenantScope.IsResolved.Should().BeFalse();
    }

    [Fact]
    public async Task AsyncFlow_PropagatesTenantId()
    {
        var tenantScope = new TenantScope();

        using var _ = TenantScope.BeginScope("async-tenant");

        await Task.Yield();

        tenantScope.TenantId.Should().Be("async-tenant");
    }

    [Fact]
    public void ImplementsITenantContext()
    {
        ITenantContext context = new TenantScope();
        context.Should().NotBeNull();
    }
}
