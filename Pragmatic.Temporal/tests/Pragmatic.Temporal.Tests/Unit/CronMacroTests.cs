using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Tests.Unit;

public class CronMacroTests
{
    [Theory]
    [InlineData("@yearly", "0 0 1 1 *")]
    [InlineData("@annually", "0 0 1 1 *")]
    [InlineData("@monthly", "0 0 1 * *")]
    [InlineData("@weekly", "0 0 * * 0")]
    [InlineData("@daily", "0 0 * * *")]
    [InlineData("@midnight", "0 0 * * *")]
    [InlineData("@hourly", "0 * * * *")]
    public void Parse_Macro_ProducesSameOccurrencesAsExpandedPattern(string macro, string expanded)
    {
        var fromMacro = CronExpression.Parse(macro);
        var fromPattern = CronExpression.Parse(expanded);

        var from = new DateTimeOffset(2026, 1, 15, 6, 30, 0, TimeSpan.Zero);
        var until = new DateTimeOffset(2027, 1, 20, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            fromPattern.GetOccurrences(from, until, maxOccurrences: 50),
            fromMacro.GetOccurrences(from, until, maxOccurrences: 50));
    }

    [Fact]
    public void Parse_Macro_ExpressionPreservesOriginalString()
    {
        Assert.Equal("@daily", CronExpression.Parse("@daily").Expression);
    }

    [Fact]
    public void Parse_MacroUppercase_IsCaseInsensitive()
    {
        var cron = CronExpression.Parse("@DAILY");

        var next = cron.GetNextOccurrence(new DateTimeOffset(2026, 1, 15, 6, 30, 0, TimeSpan.Zero));
        Assert.Equal(new DateTime(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc), next!.Value.UtcDateTime);
    }

    [Fact]
    public void Parse_Reboot_ThrowsNotSupported()
    {
        var ex = Assert.Throws<FormatException>(() => CronExpression.Parse("@reboot"));
        Assert.Contains("not supported", ex.Message);
    }

    [Fact]
    public void TryParse_UnknownMacro_ReturnsFalse()
    {
        Assert.False(CronExpression.TryParse("@foo", out var result));
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_Reboot_ReturnsFalse()
    {
        Assert.False(CronExpression.TryParse("@reboot", out _));
    }
}
