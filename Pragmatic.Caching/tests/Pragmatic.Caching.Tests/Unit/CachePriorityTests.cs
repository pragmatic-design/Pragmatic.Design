using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

public class CachePriorityTests
{
    [Theory]
    [InlineData(CachePriority.Low, 0)]
    [InlineData(CachePriority.Normal, 1)]
    [InlineData(CachePriority.High, 2)]
    [InlineData(CachePriority.NeverRemove, 3)]
    public void Values_HaveExpectedIntValues(CachePriority priority, int expected)
    {
        ((int)priority).Should().Be(expected);
    }

    [Fact]
    public void AllValues_AreDefined()
    {
        Enum.GetValues<CachePriority>().Should().HaveCount(4);
    }

    [Fact]
    public void Low_IsLessThan_Normal()
    {
        ((int)CachePriority.Low).Should().BeLessThan((int)CachePriority.Normal);
    }

    [Fact]
    public void NeverRemove_IsHighestPriority()
    {
        ((int)CachePriority.NeverRemove).Should().BeGreaterThan((int)CachePriority.High);
    }
}
