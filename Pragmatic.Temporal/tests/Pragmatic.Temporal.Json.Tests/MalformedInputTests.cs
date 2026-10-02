using Pragmatic.Testing.Assertions;
using System.Text.Json;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Json.Tests;

/// <summary>
///     Malformed payloads must surface as <see cref="JsonException" />,
///     never as silent defaults.
/// </summary>
public class MalformedInputTests
{
    private static readonly JsonSerializerOptions Options =
        new JsonSerializerOptions().AddPragmaticTemporal();

    [Theory]
    [InlineData("\"not-a-date\"")]
    [InlineData("\"2026-13-45\"")]
    public void LocalDate_Malformed_Throws(string json)
    {
        var act = () => JsonSerializer.Deserialize<LocalDate>(json, Options);

        act.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("\"25:99:00\"")]
    [InlineData("\"noon\"")]
    public void LocalTime_Malformed_Throws(string json)
    {
        var act = () => JsonSerializer.Deserialize<LocalTime>(json, Options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void LocalDateTime_Malformed_Throws()
    {
        var act = () => JsonSerializer.Deserialize<LocalDateTime>("\"garbage\"", Options);

        act.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("\"2026-01-15T10:30:00+01:00[Not/AZone]\"")]
    [InlineData("\"garbage\"")]
    public void ZonedDateTime_Malformed_Throws(string json)
    {
        var act = () => JsonSerializer.Deserialize<ZonedDateTime>(json, Options);

        act.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("\"1h30m\"")]
    [InlineData("\"P\"")]
    public void Duration_Malformed_Throws(string json)
    {
        var act = () => JsonSerializer.Deserialize<Duration>(json, Options);

        act.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("\"1 year\"")]
    [InlineData("\"P1W\"")]
    public void Period_Malformed_Throws(string json)
    {
        var act = () => JsonSerializer.Deserialize<Period>(json, Options);

        act.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("\"2026-01-01\"")]
    [InlineData("\"2026-03-31/2026-01-01\"")]
    public void DateRange_Malformed_Throws(string json)
    {
        var act = () => JsonSerializer.Deserialize<DateRange>(json, Options);

        act.Should().Throw<JsonException>();
    }
}
