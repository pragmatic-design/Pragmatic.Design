using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

public class CacheEntryOptionsTests
{
    [Fact]
    public void Default_Returns5MinuteDuration()
    {
        var options = CacheEntryOptions.Default;

        options.Duration.Should().Be(TimeSpan.FromMinutes(5));
        options.SlidingDuration.Should().BeNull();
        options.Priority.Should().Be(CachePriority.Normal);
        options.Tags.Should().BeEmpty();
    }

    [Fact]
    public void WithDuration_CreatesDurationOptions()
    {
        var options = CacheEntryOptions.WithDuration(TimeSpan.FromHours(1));

        options.Duration.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void WithSliding_CreatesSlidingOptions()
    {
        var options = CacheEntryOptions.WithSliding(TimeSpan.FromMinutes(30));

        options.SlidingDuration.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Init_AllowsCustomConfiguration()
    {
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(10),
            Priority = CachePriority.High,
            Tags = ["users", "active"]
        };

        options.Duration.Should().Be(TimeSpan.FromMinutes(10));
        options.Priority.Should().Be(CachePriority.High);
        options.Tags.Should().BeEquivalentTo("users", "active");
    }
}