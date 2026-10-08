using System.Globalization;
using System.Text;
using Pragmatic.Logging.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Providers;

/// <summary>
///     A timestamp format read once and written digit by digit gives the text <see cref="DateTime.ToString(string, IFormatProvider)" />
///     gives for that format; a format it does not read is left to <see cref="DateTime" />.
/// </summary>
public class ATimestampLayoutWritesWhatTheFormatWritesTests
{
    public static TheoryData<string> FormatsInUse =>
    [
        "yyyy-MM-ddTHH:mm:ss.fffZ",
        "yyyy-MM-dd HH:mm:ss.fff",
        "HH:mm:ss.fff",
        "HH:mm:ss",
        "HH:mm:ss.ffffff",
        "yyyy-MM-ddTHH:mm:ss.fffffffZ",
        "dd/MM/yyyy HH:mm",
        "yyyy-MM-ddTHH:mm:ss,f",
    ];

    private static readonly DateTime[] Dates =
    [
        new(1, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 10, 8, 9, 5, 3, DateTimeKind.Utc).AddTicks(457_1234),
        new(999, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc),
        DateTime.MaxValue,
    ];

    [Theory]
    [MemberData(nameof(FormatsInUse))]
    public void AFormatOfDigitsAndSeparators_IsWrittenAsTheFormatWritesIt(string format)
    {
        var layout = TimestampLayout.Parse(format);

        layout.Should().NotBeNull();
        Span<byte> buffer = stackalloc byte[64];
        foreach (var date in Dates)
        {
            layout!.TryFormat(date, buffer, out var written).Should().BeTrue();

            Encoding.UTF8.GetString(buffer[..written]).Should().Be(date.ToString(format, CultureInfo.InvariantCulture));
        }
    }

    [Theory]
    [InlineData("O")]
    [InlineData("s")]
    [InlineData("yyyy-MM-ddTHH:mm:ssK")]
    [InlineData("yyyy-MM-ddTHH:mm:sszzz")]
    [InlineData("dd MMM yyyy")]
    [InlineData("yy-MM-dd")]
    [InlineData("H:m:s")]
    [InlineData("hh:mm tt")]
    [InlineData("'at' HH:mm")]
    [InlineData("yyyy\\-MM")]
    [InlineData("")]
    public void AFormatItDoesNotRead_IsLeftToDateTime(string format)
    {
        TimestampLayout.Parse(format).Should().BeNull();
    }

    [Fact]
    public void ABufferTooSmall_IsReportedAndNothingIsClaimed()
    {
        var layout = TimestampLayout.Parse("yyyy-MM-ddTHH:mm:ss.fffZ")!;
        Span<byte> buffer = stackalloc byte[10];

        layout.TryFormat(DateTime.UtcNow, buffer, out var written).Should().BeFalse();
        written.Should().Be(0);
    }
}
