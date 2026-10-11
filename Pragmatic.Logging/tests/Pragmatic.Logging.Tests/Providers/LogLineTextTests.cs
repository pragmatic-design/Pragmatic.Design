using Pragmatic.Logging.Providers;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Logging.Tests.Providers;

/// <summary>What a value becomes on a line of text, and what it costs when there is nothing to escape.</summary>
public sealed class LogLineTextTests
{
    [Theory]
    [InlineData("a\r\nb", "a\\r\\nb")]
    [InlineData("a\rb", "a\\rb")]
    [InlineData("a\nb", "a\\nb")]
    [InlineData("a\tb", "a\\tb")]
    [InlineData("\0", "\\^@")]
    [InlineData("\u0007", "\\^G")]
    [InlineData("\u001B[31mred", "\\^[[31mred")]
    [InlineData("\u001F", "\\^_")]
    public void Escape_AControl_IsWrittenAsAnEscape(string value, string expected) =>
        LogLineText.Escape(value).Should().Be(expected);

    [Theory]
    [InlineData("/orders/42")]
    [InlineData("ünïcödé and a space")]
    [InlineData(" ~\u007F")]
    public void Escape_NoControl_ReturnsTheSameInstance(string value) =>
        ReferenceEquals(LogLineText.Escape(value), value).Should().BeTrue();

    [Fact]
    public void Escape_NullOrEmpty_IsEmpty()
    {
        LogLineText.Escape(null).Should().Be(string.Empty);
        LogLineText.Escape(string.Empty).Should().Be(string.Empty);
    }

    /// <summary>The path every entry takes: a value without controls is scanned, not copied.</summary>
    [Fact]
    public void Escape_NoControl_AllocatesNothing()
    {
        var value = "GET /orders/42?page=3 answered 200 in 12 ms";
        LogLineText.Escape(value);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
            LogLineText.Escape(value);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0);
    }
}
