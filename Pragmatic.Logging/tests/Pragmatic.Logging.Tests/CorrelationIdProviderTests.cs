using Pragmatic.Testing.Assertions;
using Pragmatic.Logging.AspNetCore;

namespace Pragmatic.Logging.Tests;

public class CorrelationIdProviderTests
{
    [Fact]
    public void IsValidCorrelationId_WithNull_ReturnsFalse()
    {
        CorrelationIdProvider.IsValidCorrelationId(null).Should().BeFalse();
    }

    [Fact]
    public void IsValidCorrelationId_WithEmpty_ReturnsFalse()
    {
        CorrelationIdProvider.IsValidCorrelationId(string.Empty).Should().BeFalse();
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("ABC-123_xyz.tag")]
    [InlineData("a")]
    [InlineData("550e8400-e29b-41d4-a716-446655440000")]
    public void IsValidCorrelationId_WithSafeCharset_ReturnsTrue(string value)
    {
        CorrelationIdProvider.IsValidCorrelationId(value).Should().BeTrue();
    }

    [Theory]
    [InlineData("abc 123")]      // space
    [InlineData("abc/123")]      // slash
    [InlineData("abc:123")]      // colon
    [InlineData("abc\r\nLF")]    // CRLF — header injection
    [InlineData("<script>")]     // angle brackets
    [InlineData("café")]         // non-ASCII
    public void IsValidCorrelationId_WithUnsafeCharacters_ReturnsFalse(string value)
    {
        CorrelationIdProvider.IsValidCorrelationId(value).Should().BeFalse();
    }

    [Fact]
    public void IsValidCorrelationId_AtMaxLength_ReturnsTrue()
    {
        var value = new string('a', CorrelationIdProvider.MaxCorrelationIdLength);

        CorrelationIdProvider.IsValidCorrelationId(value).Should().BeTrue();
    }

    [Fact]
    public void IsValidCorrelationId_ExceedingMaxLength_ReturnsFalse()
    {
        var value = new string('a', CorrelationIdProvider.MaxCorrelationIdLength + 1);

        CorrelationIdProvider.IsValidCorrelationId(value).Should().BeFalse();
    }
}
