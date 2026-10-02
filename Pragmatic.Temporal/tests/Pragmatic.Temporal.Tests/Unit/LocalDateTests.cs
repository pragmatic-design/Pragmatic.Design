using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Types;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

public class LocalDateTests
{
    [Fact]
    public void Constructor_WithValidDate_CreatesInstance()
    {
        var date = new LocalDate(2024, 6, 15);

        date.Year.Should().Be(2024);
        date.Month.Should().Be(6);
        date.Day.Should().Be(15);
    }

    [Fact]
    public void AddDays_WithPositiveValue_ReturnsCorrectDate()
    {
        var date = new LocalDate(2024, 6, 15);

        var result = date.AddDays(10);

        result.Should().Be(new LocalDate(2024, 6, 25));
    }

    [Fact]
    public void AddDays_AcrossMonthBoundary_ReturnsCorrectDate()
    {
        var date = new LocalDate(2024, 6, 25);

        var result = date.AddDays(10);

        result.Should().Be(new LocalDate(2024, 7, 5));
    }

    [Fact]
    public void AddMonths_WithPositiveValue_ReturnsCorrectDate()
    {
        var date = new LocalDate(2024, 6, 15);

        var result = date.AddMonths(3);

        result.Should().Be(new LocalDate(2024, 9, 15));
    }

    [Fact]
    public void DayOfWeek_ReturnsCorrectDay()
    {
        // June 15, 2024 is a Saturday
        var date = new LocalDate(2024, 6, 15);

        date.DayOfWeek.Should().Be(DayOfWeek.Saturday);
    }

    [Fact]
    public void Equality_WithSameDate_ReturnsTrue()
    {
        var date1 = new LocalDate(2024, 6, 15);
        var date2 = new LocalDate(2024, 6, 15);

        (date1 == date2).Should().BeTrue();
        date1.Equals(date2).Should().BeTrue();
    }

    [Fact]
    public void CompareTo_WithEarlierDate_ReturnsPositive()
    {
        var date1 = new LocalDate(2024, 6, 15);
        var date2 = new LocalDate(2024, 6, 10);

        date1.CompareTo(date2).Should().BePositive();
        (date1 > date2).Should().BeTrue();
    }

    [Fact]
    public void At_WithLocalTime_ReturnsLocalDateTime()
    {
        var date = new LocalDate(2024, 6, 15);
        var time = new LocalTime(14, 30);

        var result = date.At(time);

        result.Year.Should().Be(2024);
        result.Month.Should().Be(6);
        result.Day.Should().Be(15);
        result.Hour.Should().Be(14);
        result.Minute.Should().Be(30);
    }

    [Fact]
    public void ToString_ReturnsIso8601Format()
    {
        var date = new LocalDate(2024, 6, 15);

        date.ToString().Should().Be("2024-06-15");
    }
}