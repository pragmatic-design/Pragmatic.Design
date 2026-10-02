using Pragmatic.Testing.Assertions;

namespace Pragmatic.MultiTenancy.Tests.Unit;

public class MutableTenantContextTests
{
    [Fact]
    public void Default_IsNotResolved()
    {
        var context = new MutableTenantContext();

        context.TenantId.Should().BeNull();
        context.TenantName.Should().BeNull();
        context.IsResolved.Should().BeFalse();
    }

    [Fact]
    public void WhenTenantIdSet_IsResolved()
    {
        var context = new MutableTenantContext
        {
            TenantId = "tenant-1",
            TenantName = "Acme Corp"
        };

        context.TenantId.Should().Be("tenant-1");
        context.TenantName.Should().Be("Acme Corp");
        context.IsResolved.Should().BeTrue();
    }

    [Fact]
    public void EmptyTenantId_IsNotResolved()
    {
        var context = new MutableTenantContext { TenantId = "" };

        context.IsResolved.Should().BeFalse();
    }

    [Fact]
    public void ImplementsITenantContext()
    {
        ITenantContext context = new MutableTenantContext();
        context.Should().NotBeNull();
    }
}
