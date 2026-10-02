using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pragmatic.MultiTenancy.Tests.Unit;

public class CompositeTenantResolverTests
{
    private static readonly NullLogger<CompositeTenantResolver> Logger = new();

    [Fact]
    public async Task NoResolvers_ReturnsNull()
    {
        var composite = new CompositeTenantResolver([], Logger);

        var result = await composite.ResolveAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task FirstResolverSucceeds_ReturnsItsValue()
    {
        var resolvers = new ITenantResolver[]
        {
            new SingleTenantResolver("first"),
            new SingleTenantResolver("second")
        };
        var composite = new CompositeTenantResolver(resolvers, Logger);

        var result = await composite.ResolveAsync();

        result.Should().Be("first");
    }

    [Fact]
    public async Task FirstReturnsNull_FallsToSecond()
    {
        var resolvers = new ITenantResolver[]
        {
            new NullResolver(),
            new SingleTenantResolver("fallback")
        };
        var composite = new CompositeTenantResolver(resolvers, Logger);

        var result = await composite.ResolveAsync();

        result.Should().Be("fallback");
    }

    [Fact]
    public async Task AllReturnNull_ReturnsNull()
    {
        var resolvers = new ITenantResolver[]
        {
            new NullResolver(),
            new NullResolver()
        };
        var composite = new CompositeTenantResolver(resolvers, Logger);

        var result = await composite.ResolveAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task FailingResolver_ContinuesToNext()
    {
        var resolvers = new ITenantResolver[]
        {
            new ThrowingResolver(),
            new SingleTenantResolver("recovered")
        };
        var composite = new CompositeTenantResolver(resolvers, Logger);

        var result = await composite.ResolveAsync();

        result.Should().Be("recovered");
    }

    private sealed class NullResolver : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(null);
    }

    private sealed class ThrowingResolver : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Resolver failure");
    }
}
