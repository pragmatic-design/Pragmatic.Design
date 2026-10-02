using Pragmatic.Testing.Assertions;
using Pragmatic.Endpoints.Configuration;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

public class RateLimiterConfigurationTests
{
    [Fact]
    public void TokensPerPeriod_Default_FallsBackToPermitLimit()
    {
        var config = new RateLimiterConfiguration { PermitLimit = 42 };

        config.TokensPerPeriod.Should().Be(42);
    }

    [Fact]
    public void TokensPerPeriod_ExplicitPositive_ReturnsThatValue()
    {
        var config = new RateLimiterConfiguration { PermitLimit = 42, TokensPerPeriod = 7 };

        config.TokensPerPeriod.Should().Be(7);
    }

    [Fact]
    public void TokensPerPeriod_SetToZero_FallsBackToPermitLimit()
    {
        var config = new RateLimiterConfiguration { PermitLimit = 10, TokensPerPeriod = 0 };

        config.TokensPerPeriod.Should().Be(10);
    }

    [Fact]
    public void TokensPerPeriod_SetNegative_ThrowsArgumentOutOfRange()
    {
        var config = new RateLimiterConfiguration();

        var act = () => config.TokensPerPeriod = -1;

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Strategy_Default_IsFixedWindow()
    {
        var config = new RateLimiterConfiguration();

        config.Strategy.Should().Be(RateLimiterStrategy.FixedWindow);
    }

    [Fact]
    public void PermitLimit_Default_Is10()
    {
        var config = new RateLimiterConfiguration();

        config.PermitLimit.Should().Be(10);
    }

    [Fact]
    public void Window_Default_IsOneMinute()
    {
        var config = new RateLimiterConfiguration();

        config.Window.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void AutoReplenishment_Default_IsTrue()
    {
        var config = new RateLimiterConfiguration();

        config.AutoReplenishment.Should().BeTrue();
    }
}
