using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Identifiers;

namespace Pragmatic.Persistence.Tests.Identifiers;

public class GuidExtensionsTests
{
    [Fact]
    public void ToShortString_Returns22Chars()
    {
        var guid = Guid.NewGuid();

        var result = guid.ToShortString();

        result.Should().HaveLength(22);
    }

    [Fact]
    public void ToShortString_MatchesShortGuidEncode()
    {
        var guid = Guid.NewGuid();

        var fromExtension = guid.ToShortString();
        var fromShortGuid = ShortGuid.Encode(guid);

        fromExtension.Should().Be(fromShortGuid);
    }

    [Fact]
    public void IsEmpty_WithEmptyGuid_ReturnsTrue()
    {
        Guid.Empty.IsEmpty().Should().BeTrue();
    }

    [Fact]
    public void IsEmpty_WithNonEmptyGuid_ReturnsFalse()
    {
        Guid.NewGuid().IsEmpty().Should().BeFalse();
    }

    [Fact]
    public void IsGuid7_WithGuid7_ReturnsTrue()
    {
        var guid = Guid7.New();

        guid.IsGuid7().Should().BeTrue();
    }

    [Fact]
    public void IsGuid7_WithGuidV4_ReturnsFalse()
    {
        var guid = Guid.NewGuid();

        guid.IsGuid7().Should().BeFalse();
    }

    [Fact]
    public void GetTimestamp_WithGuid7_ReturnsTimestamp()
    {
        var guid = Guid7.New();

        guid.GetTimestamp().Should().NotBeNull();
    }

    [Fact]
    public void GetTimestamp_WithNonGuid7_ReturnsNull()
    {
        var guid = Guid.NewGuid();

        guid.GetTimestamp().Should().BeNull();
    }
}
