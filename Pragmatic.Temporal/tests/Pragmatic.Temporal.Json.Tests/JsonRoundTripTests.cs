using Pragmatic.Testing.Assertions;
using System.Text.Json;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Json.Tests;

/// <summary>
///     Serialize → deserialize round-trips for every temporal converter,
///     including nullable variants.
/// </summary>
public class JsonRoundTripTests
{
    private static readonly JsonSerializerOptions Options =
        new JsonSerializerOptions().AddPragmaticTemporal();

    private static T RoundTrip<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        return JsonSerializer.Deserialize<T>(json, Options)!;
    }

    [Fact]
    public void LocalDate_RoundTrip_PreservesValue()
    {
        var value = new LocalDate(2026, 6, 15);

        JsonSerializer.Serialize(value, Options).Should().Be("\"2026-06-15\"");
        RoundTrip(value).Should().Be(value);
    }

    [Fact]
    public void LocalTime_RoundTrip_PreservesValue()
    {
        var value = new LocalTime(14, 30, 45);

        JsonSerializer.Serialize(value, Options).Should().Be("\"14:30:45\"");
        RoundTrip(value).Should().Be(value);
    }

    [Fact]
    public void LocalDateTime_RoundTrip_PreservesValue()
    {
        var value = new LocalDateTime(2026, 6, 15, 14, 30, 45);

        JsonSerializer.Serialize(value, Options).Should().Be("\"2026-06-15T14:30:45\"");
        RoundTrip(value).Should().Be(value);
    }

    [Fact]
    public void ZonedDateTime_RoundTrip_PreservesInstantAndZone()
    {
        var value = ZonedDateTime.FromUtc(
            new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero), "Europe/Rome");

        var json = JsonSerializer.Serialize(value, Options);

        json.Should().Contain("[Europe/Rome]");
        var back = JsonSerializer.Deserialize<ZonedDateTime>(json, Options);
        back.EqualsExact(value).Should().BeTrue();
    }

    [Fact]
    public void Duration_RoundTrip_PreservesValue()
    {
        var value = Duration.FromHours(1.5);

        JsonSerializer.Serialize(value, Options).Should().Be("\"PT1H30M\"");
        RoundTrip(value).Should().Be(value);
    }

    [Fact]
    public void Period_RoundTrip_PreservesValue()
    {
        var value = new Period(1, 2, 3);

        JsonSerializer.Serialize(value, Options).Should().Be("\"P1Y2M3D\"");
        RoundTrip(value).Should().Be(value);
    }

    [Fact]
    public void DateRange_RoundTrip_PreservesValue()
    {
        var value = new DateRange(new LocalDate(2026, 1, 1), new LocalDate(2026, 3, 31));

        (RoundTrip(value) == value).Should().BeTrue();
    }

    [Fact]
    public void CronExpression_RoundTrip_PreservesExpression()
    {
        var value = CronExpression.Parse("*/15 9-17 * * 1-5");

        RoundTrip(value).Expression.Should().Be(value.Expression);
    }

    [Fact]
    public void NullableVariants_Null_RoundTripsAsNull()
    {
        JsonSerializer.Serialize<LocalDate?>(null, Options).Should().Be("null");
        JsonSerializer.Deserialize<LocalDate?>("null", Options).Should().BeNull();
        JsonSerializer.Deserialize<LocalTime?>("null", Options).Should().BeNull();
        JsonSerializer.Deserialize<LocalDateTime?>("null", Options).Should().BeNull();
        JsonSerializer.Deserialize<ZonedDateTime?>("null", Options).Should().BeNull();
        JsonSerializer.Deserialize<Duration?>("null", Options).Should().BeNull();
        JsonSerializer.Deserialize<Period?>("null", Options).Should().BeNull();
        JsonSerializer.Deserialize<DateRange?>("null", Options).Should().BeNull();
    }

    [Fact]
    public void NullableVariants_Value_RoundTripsValue()
    {
        LocalDate? date = new LocalDate(2026, 6, 15);
        Duration? duration = Duration.FromMinutes(90);

        RoundTrip(date).Should().Be(date);
        RoundTrip(duration).Should().Be(duration);
    }
}
