using Pragmatic.Testing.Assertions;
using Pragmatic.Caching;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class CacheEntryOptionsTests
{
    [Fact]
    public void Default_HasFiveMinuteDuration()
        => CacheEntryOptions.Default.Duration.Should().Be(TimeSpan.FromMinutes(5),
            "the 5-minute default is a documented contract consumers rely on");

    [Fact]
    public void WithDuration_SetsAbsoluteDuration()
    {
        var options = CacheEntryOptions.WithDuration(TimeSpan.FromSeconds(30));

        options.Duration.Should().Be(TimeSpan.FromSeconds(30));
        options.SlidingDuration.Should().BeNull();
    }

    [Fact]
    public void WithSliding_SetsSlidingDuration()
    {
        var options = CacheEntryOptions.WithSliding(TimeSpan.FromMinutes(1));

        options.SlidingDuration.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Priority_DefaultsToNormal()
        => new CacheEntryOptions().Priority.Should().Be(CachePriority.Normal);
}
