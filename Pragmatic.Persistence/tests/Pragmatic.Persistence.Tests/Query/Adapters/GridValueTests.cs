using System.Text;
using System.Text.Json;
using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.Tests.AdapterTests;

/// <summary>
///     <see cref="GridValue" /> reads a canonical clause's value as JSON brings it (#325): every type the generated
///     bridge declares filterable, and an answer that tells no value from an unusable one.
/// </summary>
public class GridValueTests
{
    /// <summary>
    ///     The bridge declares these filterable, and they were read as nothing: a clause on a <c>DateOnly</c> column was
    ///     dropped, whatever the client sent.
    /// </summary>
    [Fact]
    public void Every_type_the_bridge_filters_on_is_read()
    {
        GridValue.As<short?>(Json("12")).Should().Be((short)12);
        GridValue.As<byte?>(Json("200")).Should().Be((byte)200);
        GridValue.As<float?>(Json("1.5")).Should().Be(1.5f);
        GridValue.As<DateOnly?>(Json("\"2026-10-20\"")).Should().Be(new DateOnly(2026, 10, 20));
        GridValue.As<TimeOnly?>(Json("\"13:45\"")).Should().Be(new TimeOnly(13, 45));
        GridValue.As<TimeSpan?>(Json("\"01:30:00\"")).Should().Be(TimeSpan.FromMinutes(90));
        GridValue.As<decimal?>(Json("\"12.5\"")).Should().Be(12.5m);
        GridValue.As<Guid?>(Json("\"0f8fad5b-d9cb-469f-a165-70867728950e\"")).Should().Be(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));
    }

    [Theory]
    [InlineData("null")]
    public void No_value_is_a_value_read_as_nothing(string json)
    {
        GridValue.TryAs<int?>(Json(json), out var value).Should().BeTrue();
        value.Should().BeNull();
        GridValue.IsNull(Json(json)).Should().BeTrue();
        GridValue.IsNull(null).Should().BeTrue();
    }

    /// <summary>
    ///     A value of another type, past the range, a list where one value goes: not a value, and not nothing either.
    ///     <see cref="GridValue.As{T}" /> answers <c>default</c> for both, and the bridge dropped the clause.
    /// </summary>
    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("99999999999")]
    [InlineData("[1,2]")]
    [InlineData("{\"x\":1}")]
    [InlineData("1.5")]
    public void A_value_that_is_not_an_int_is_refused_not_read_as_nothing(string json)
        => GridValue.TryAs<int?>(Json(json), out _).Should().BeFalse();

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("{\"x\":1}")]
    public void A_list_or_an_object_is_not_a_string(string json)
        => GridValue.TryAs<string?>(Json(json), out _).Should().BeFalse();

    /// <summary>A string whose bytes are not UTF-8 is refused, where GetString would have thrown out of the bridge.</summary>
    [Fact]
    public void A_string_that_is_not_utf8_is_refused()
    {
        var bytes = Encoding.UTF8.GetBytes("\"\u0001abc\"").Select(b => b == 0x01 ? (byte)0x9A : b).ToArray();
        using var document = JsonDocument.Parse(bytes);

        GridValue.TryAs<string?>(document.RootElement.Clone(), out _).Should().BeFalse();
        GridValue.TryAs<DateOnly?>(document.RootElement.Clone(), out _).Should().BeFalse();
    }

    [Fact]
    public void The_values_of_an_in_are_the_elements_of_a_list_and_a_string_is_not_one()
    {
        GridValue.Elements(Json("[1,2,3]")).Should().HaveCount(3);
        GridValue.Elements(new[] { 1, 2 }).Should().HaveCount(2);
        GridValue.Elements("abc").Should().BeNull();
        GridValue.Elements(Json("3")).Should().BeNull();
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
