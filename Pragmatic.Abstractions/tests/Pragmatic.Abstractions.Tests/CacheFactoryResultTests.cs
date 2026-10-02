using Pragmatic.Testing.Assertions;
using Pragmatic.Caching;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class CacheFactoryResultTests
{
    [Fact]
    public void Cache_SetsShouldCacheTrue()
    {
        var result = CacheFactoryResult<int>.Cache(42);

        result.Value.Should().Be(42);
        result.ShouldCache.Should().BeTrue();
    }

    [Fact]
    public void DoNotCache_SetsShouldCacheFalse()
    {
        var result = CacheFactoryResult<int>.DoNotCache(42);

        result.Value.Should().Be(42);
        result.ShouldCache.Should().BeFalse();
    }

    [Fact]
    public void ImplicitConversion_FromValue_MeansCache()
    {
        CacheFactoryResult<string> result = "hello";

        result.Value.Should().Be("hello");
        result.ShouldCache.Should().BeTrue();
    }
}
