using Pragmatic.Testing.Assertions;
using System.Text.Json;
using Pragmatic.Temporal.Json.Converters;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Json.Tests;

public class ZonedDateTimeConverterJsonTests
{
    private static readonly JsonSerializerOptions StringOptions =
        new JsonSerializerOptions().AddPragmaticTemporal();

    private static readonly ZonedDateTime Sample = ZonedDateTime.FromUtc(
        new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero), "Europe/Rome");

    [Fact]
    public void Write_DefaultMode_ProducesBracketString()
    {
        var json = JsonSerializer.Serialize(Sample, StringOptions);

        // Deserialize as string to undo STJ's default escaping of '+'.
        JsonSerializer.Deserialize<string>(json)
            .Should().Be("2026-01-15T10:30:00+01:00[Europe/Rome]");
    }

    [Fact]
    public void Write_ObjectMode_ProducesUtcZoneLocal()
    {
        var objectOptions = new JsonSerializerOptions();
        objectOptions.Converters.Add(new ZonedDateTimeConverter { WriteAsString = false });

        var json = JsonSerializer.Serialize(Sample, objectOptions);

        json.Should().Contain("\"utc\"").And.Contain("\"zone\"").And.Contain("Europe/Rome");
    }

    [Fact]
    public void Read_ObjectWithZoneKey_Parses()
    {
        var json = "{\"utc\":\"2026-01-15T09:30:00.0000000+00:00\",\"zone\":\"Europe/Rome\"}";

        var result = JsonSerializer.Deserialize<ZonedDateTime>(json, StringOptions);

        result.EqualsExact(Sample).Should().BeTrue();
    }

    [Fact]
    public void Read_ObjectWithTimezoneKey_Parses()
    {
        var json = "{\"utc\":\"2026-01-15T09:30:00.0000000+00:00\",\"timezone\":\"Europe/Rome\"}";

        var result = JsonSerializer.Deserialize<ZonedDateTime>(json, StringOptions);

        result.EqualsExact(Sample).Should().BeTrue();
    }

    [Fact]
    public void Read_ObjectMissingZone_Throws()
    {
        var json = "{\"utc\":\"2026-01-15T09:30:00.0000000+00:00\"}";

        var act = () => JsonSerializer.Deserialize<ZonedDateTime>(json, StringOptions);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_StringMode_RoundTripsObjectModeOutput()
    {
        var objectOptions = new JsonSerializerOptions();
        objectOptions.Converters.Add(new ZonedDateTimeConverter { WriteAsString = false });
        var json = JsonSerializer.Serialize(Sample, objectOptions);

        var back = JsonSerializer.Deserialize<ZonedDateTime>(json, StringOptions);

        back.EqualsExact(Sample).Should().BeTrue();
    }
}
