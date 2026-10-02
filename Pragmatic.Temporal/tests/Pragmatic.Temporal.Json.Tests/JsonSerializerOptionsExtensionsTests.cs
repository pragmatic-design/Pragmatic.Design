using Pragmatic.Testing.Assertions;
using System.Text.Json;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Json.Tests;

public class JsonSerializerOptionsExtensionsTests
{
    [Fact]
    public void AddPragmaticTemporal_RegistersConvertersForAllTemporalTypes()
    {
        var options = new JsonSerializerOptions().AddPragmaticTemporal();

        options.GetConverter(typeof(LocalDate)).Should().NotBeNull();
        options.GetConverter(typeof(LocalDate?)).Should().NotBeNull();
        options.GetConverter(typeof(LocalTime)).Should().NotBeNull();
        options.GetConverter(typeof(LocalDateTime)).Should().NotBeNull();
        options.GetConverter(typeof(ZonedDateTime)).Should().NotBeNull();
        options.GetConverter(typeof(Duration)).Should().NotBeNull();
        options.GetConverter(typeof(Period)).Should().NotBeNull();
        options.GetConverter(typeof(DateRange)).Should().NotBeNull();
        options.GetConverter(typeof(CronExpression)).Should().NotBeNull();
    }

    [Fact]
    public void AddPragmaticTemporal_ReturnsSameInstanceForChaining()
    {
        var options = new JsonSerializerOptions();

        options.AddPragmaticTemporal().Should().BeSameAs(options);
    }

    [Fact]
    public void CreateTemporalOptions_UsesCamelCaseNaming()
    {
        var options = JsonSerializerOptionsExtensions.CreateTemporalOptions();

        var json = JsonSerializer.Serialize(new { StartDate = new LocalDate(2026, 1, 1) }, options);

        json.Should().Contain("\"startDate\"");
    }

    [Fact]
    public void LocalDateTime_InputWithTimezoneSuffix_StripsSuffix()
    {
        var options = new JsonSerializerOptions().AddPragmaticTemporal();

        var result = JsonSerializer.Deserialize<LocalDateTime>(
            "\"2026-06-15T14:30:00+05:00\"", options);

        // The offset is discarded: LocalDateTime is a wall-clock value.
        result.Should().Be(new LocalDateTime(2026, 6, 15, 14, 30));
    }
}
