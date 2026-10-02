using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

public class CachingOptionsTests
{
    [Fact]
    public void DefaultDuration_Is5Minutes()
    {
        var options = new CachingOptions();

        options.DefaultDuration.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void EnableQueryCaching_DefaultIsTrue()
    {
        var options = new CachingOptions();

        options.EnableQueryCaching.Should().BeTrue();
    }

    [Fact]
    public void EnableEventInvalidation_DefaultIsTrue()
    {
        var options = new CachingOptions();

        options.EnableEventInvalidation.Should().BeTrue();
    }

    [Fact]
    public void DefaultDuration_CanBeModified()
    {
        var options = new CachingOptions { DefaultDuration = TimeSpan.FromHours(1) };

        options.DefaultDuration.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void EnableQueryCaching_CanBeDisabled()
    {
        var options = new CachingOptions { EnableQueryCaching = false };

        options.EnableQueryCaching.Should().BeFalse();
    }

    [Fact]
    public void EnableEventInvalidation_CanBeDisabled()
    {
        var options = new CachingOptions { EnableEventInvalidation = false };

        options.EnableEventInvalidation.Should().BeFalse();
    }
}
