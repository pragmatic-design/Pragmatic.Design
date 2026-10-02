using Pragmatic.Testing.Assertions;
using System.Text.Json;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Json.Tests;

public class CronExpressionConverterJsonTests
{
    private static readonly JsonSerializerOptions Options =
        new JsonSerializerOptions().AddPragmaticTemporal();

    [Fact]
    public void Write_ProducesRawExpressionString()
    {
        var cron = CronExpression.Parse("0 9 * * 1-5");

        JsonSerializer.Serialize(cron, Options).Should().Be("\"0 9 * * 1-5\"");
    }

    [Fact]
    public void Read_ValidExpression_Parses()
    {
        var result = JsonSerializer.Deserialize<CronExpression>("\"*/15 * * * *\"", Options);

        result!.Expression.Should().Be("*/15 * * * *");
    }

    [Fact]
    public void Read_Macro_ParsesAndPreservesOriginalString()
    {
        var result = JsonSerializer.Deserialize<CronExpression>("\"@daily\"", Options);

        result!.Expression.Should().Be("@daily");
    }

    [Fact]
    public void Read_EmptyString_ThrowsJsonException()
    {
        var act = () => JsonSerializer.Deserialize<CronExpression>("\"\"", Options);

        act.Should().Throw<JsonException>().WithMessage("*empty*");
    }

    [Fact]
    public void Read_WhitespaceString_ThrowsJsonException()
    {
        var act = () => JsonSerializer.Deserialize<CronExpression>("\"   \"", Options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_InvalidExpression_ThrowsJsonException()
    {
        var act = () => JsonSerializer.Deserialize<CronExpression>("\"not a cron\"", Options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_NullToken_ReturnsNullForReferenceType()
    {
        var result = JsonSerializer.Deserialize<CronExpression>("null", Options);

        result.Should().BeNull();
    }
}
