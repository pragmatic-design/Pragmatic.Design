using Pragmatic.Testing.Assertions;

namespace Pragmatic.MultiTenancy.Tests.Unit;

public class SingleTenantResolverTests
{
    [Fact]
    public async Task Default_ReturnsDefaultTenantId()
    {
        var resolver = new SingleTenantResolver();

        var result = await resolver.ResolveAsync();

        result.Should().Be("default");
    }

    [Fact]
    public async Task CustomTenantId_ReturnsConfiguredValue()
    {
        var resolver = new SingleTenantResolver("my-tenant");

        var result = await resolver.ResolveAsync();

        result.Should().Be("my-tenant");
    }

    [Fact]
    public void ImplementsITenantResolver()
    {
        ITenantResolver resolver = new SingleTenantResolver();
        resolver.Should().NotBeNull();
    }
}
