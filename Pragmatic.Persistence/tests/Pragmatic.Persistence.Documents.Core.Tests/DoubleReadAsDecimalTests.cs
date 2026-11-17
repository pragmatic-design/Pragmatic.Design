using Pragmatic.Persistence.Documents.Mongo.Bson;
using Xunit;

namespace Pragmatic.Persistence.Documents.Core.Tests;

/// <summary>
///     A number another program stored as a BSON double, read where the model has a decimal (#831): the decimal its shortest
///     round-trip text spells, the number a person wrote, whatever runtime the application runs on.
/// </summary>
/// <remarks>
///     .NET 11 made the cast exact: <c>(decimal)19.99</c> is <c>19.989999999999998436805981328</c> there, <c>19.99</c> on .NET 10. The
///     seventeen-digit case fails on .NET 10 as well, where the cast keeps fifteen significant digits, so the suite sees the cast on
///     either runtime.
/// </remarks>
public class DoubleReadAsDecimalTests
{
    public static TheoryData<double, decimal> Numbers => new()
    {
        { 19.99, 19.99m },
        { 0.1, 0.1m },
        { 1.23, 1.23m },
        { -42.5, -42.5m },
        { 0.12345678901234568, 0.12345678901234568m },
    };

    [Theory]
    [MemberData(nameof(Numbers))]
    public void A_stored_double_reads_as_the_decimal_it_spells(double stored, decimal expected)
        => Assert.Equal(expected, ReadDecimalField(DocumentWith(stored)));

    [Theory]
    [MemberData(nameof(Numbers))]
    public void An_aggregate_returned_as_a_double_reads_as_the_decimal_it_spells(double stored, decimal expected)
    {
        Assert.Equal(expected, DocValues.ReadScalar<decimal>(DocumentWith(stored), "v"));
        Assert.Equal(expected, DocValues.ReadScalar<decimal?>(DocumentWith(stored), "v"));
    }

    [Fact]
    public void A_double_past_the_decimal_range_still_overflows()
    {
        Assert.Throws<OverflowException>(() => ReadDecimalField(DocumentWith(1e30)));
        Assert.Throws<OverflowException>(() => DocValues.ReadScalar<decimal>(DocumentWith(1e30), "v"));
    }

    [Fact]
    public void A_double_that_is_not_a_number_overflows_as_the_cast_did()
        => Assert.Throws<OverflowException>(() => ReadDecimalField(DocumentWith(double.NaN)));

    private static decimal ReadDecimalField(BsonDocument document)
    {
        var reader = document.Reader();
        Assert.True(reader.ReadElement(out var type, out _));
        Assert.Equal(BsonType.Double, type);
        return DocMapping.ReadDecimal(ref reader);
    }

    private static BsonDocument DocumentWith(double value)
    {
        var writer = new BsonWriter();
        writer.StartDocument();
        writer.Write("v", value);
        writer.EndDocument();
        return new BsonDocument(writer.Written.ToArray());
    }
}
