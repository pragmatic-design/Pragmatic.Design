using Pragmatic.Testing.Assertions;
using Pragmatic.Events.EFCore.Outbox;

namespace Pragmatic.Events.Tests.EFCore;

/// <summary>
///     Lower-bound validation on <see cref="EventOutboxOptions" />: misconfigured values that would
///     silently break the delivery loop (a zero batch, a non-positive interval, a zero attempt ceiling)
///     are rejected at assignment rather than tolerated.
/// </summary>
public sealed class EventOutboxOptionsTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var options = new EventOutboxOptions();

        options.BatchSize.Should().Be(100);
        options.PollingInterval.Should().Be(TimeSpan.FromSeconds(5));
        options.MaxAttempts.Should().Be(5);
        options.ClaimLeaseDuration.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void ClaimLeaseDuration_Zero_Throws()
    {
        var act = () => new EventOutboxOptions { ClaimLeaseDuration = TimeSpan.Zero };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BatchSize_LessThanOne_Throws(int value)
    {
        var act = () => new EventOutboxOptions { BatchSize = value };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxAttempts_LessThanOne_Throws(int value)
    {
        var act = () => new EventOutboxOptions { MaxAttempts = value };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PollingInterval_Zero_Throws()
    {
        var act = () => new EventOutboxOptions { PollingInterval = TimeSpan.Zero };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PollingInterval_Negative_Throws()
    {
        var act = () => new EventOutboxOptions { PollingInterval = TimeSpan.FromSeconds(-1) };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
