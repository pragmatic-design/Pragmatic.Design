using System.Buffers.Binary;
using Pragmatic.Persistence.Documents.Mongo.Bson;
using Xunit;

namespace Pragmatic.Persistence.Documents.Core.Tests;

/// <summary>BSON written and read back without a server: the specification's own example, every type this driver writes, and decimal128 in both directions.</summary>
public class BsonTests
{
    [Fact]
    public void The_specification_example_is_written_byte_for_byte()
    {
        // bsonspec.org: {"hello": "world"}
        var writer = new BsonWriter();
        writer.StartDocument();
        writer.Write("hello", "world");
        writer.EndDocument();
        Assert.Equal(Convert.FromHexString("160000000268656C6C6F0006000000776F726C640000"), writer.Written.ToArray());
    }

    [Fact]
    public void Every_type_reads_back_as_it_was_written()
    {
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 20, 9, 30, 15, 123, TimeSpan.Zero);
        var writer = new BsonWriter();
        writer.StartDocument();
        writer.Write("s", "città");
        writer.Write("i", int.MinValue);
        writer.Write("l", long.MaxValue);
        writer.Write("d", 0.1);
        writer.Write("b", true);
        writer.Write("m", -79228162514264337593543950335m);
        writer.Write("g", id);
        writer.Write("t", at);
        writer.WriteNull("n");
        writer.StartDocument("o");
        writer.Write("x", 1);
        writer.EndDocument();
        writer.StartArray("a");
        writer.Write(0, "zero");
        writer.Write(1, 1);
        writer.EndArray();
        writer.EndDocument();

        var reader = new BsonReader(writer.Written.Span);
        var seen = new List<string>();
        while (reader.ReadElement(out var type, out var name))
        {
            var key = System.Text.Encoding.UTF8.GetString(name);
            seen.Add(key);
            switch (key)
            {
                case "s": Assert.Equal("città", reader.ReadString()); break;
                case "i": Assert.Equal(int.MinValue, reader.ReadInt32()); break;
                case "l": Assert.Equal(long.MaxValue, reader.ReadInt64()); break;
                case "d": Assert.Equal(0.1, reader.ReadDouble()); break;
                case "b": Assert.True(reader.ReadBoolean()); break;
                case "m": Assert.Equal(-79228162514264337593543950335m, reader.ReadDecimal()); break;
                case "g": Assert.Equal(id, reader.ReadGuid()); break;
                case "t": Assert.Equal(at, reader.ReadDateTime()); break;
                case "n": Assert.Equal(BsonType.Null, type); break;
                case "o":
                    var inner = new BsonReader(reader.ReadDocument());
                    Assert.True(inner.ReadElement(out _, out var innerName));
                    Assert.True(innerName.SequenceEqual("x"u8));
                    Assert.Equal(1, inner.ReadInt32());
                    Assert.False(inner.ReadElement(out _, out _));
                    break;
                case "a":
                    var array = new BsonDocument(reader.ReadDocument().ToArray());
                    Assert.Equal(["0", "1"], array.Names());
                    Assert.True(array.TryGetString("0", out var zero) && zero == "zero");
                    break;
                default: throw new InvalidOperationException(key);
            }
        }

        Assert.Equal(["s", "i", "l", "d", "b", "m", "g", "t", "n", "o", "a"], seen);
    }

    [Fact]
    public void A_guid_is_subtype_4_in_rfc_4122_order()
    {
        var id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var writer = new BsonWriter();
        writer.StartDocument();
        writer.Write("g", id);
        writer.EndDocument();
        var bytes = writer.Written.Span;
        Assert.Equal(BsonBinarySubtype.Uuid, bytes[4 + 1 + 2 + 4]); // type, "g\0", length, then the subtype
        Assert.Equal(Convert.FromHexString("00112233445566778899AABBCCDDEEFF"), bytes.Slice(4 + 1 + 2 + 4 + 1, 16).ToArray());
    }

    [Theory]
    [InlineData("1", "0100000000000000", "0000000000004030")] // the BSON corpus' canonical encodings (decimal128-1.json)
    [InlineData("-1", "0100000000000000", "00000000000040B0")]
    [InlineData("0.1", "0100000000000000", "0000000000003E30")]
    [InlineData("0", "0000000000000000", "0000000000004030")]
    public void Decimal128_matches_the_canonical_encoding(string value, string lowHex, string highHex)
    {
        Span<byte> bytes = stackalloc byte[16];
        Decimal128.Write(bytes, decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(lowHex + highHex, Convert.ToHexString(bytes));
        Assert.Equal(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), Decimal128.Read(bytes));
    }

    [Fact]
    public void Decimal128_rounds_half_to_even_when_a_decimal_cannot_keep_every_digit()
    {
        // 34 digits, exponent -33: 1.234…; a decimal keeps 28 decimals (and 29 significant digits at most).
        Assert.Equal(1.2345678901234567890123456789m, Read(UInt128.Parse("1234567890123456789012345678901234"), -33));
        Assert.Equal(0.0000000000000000000000000002m, Read(25, -29)); // 2.5e-28: the half goes to the even 2
        Assert.Equal(0.0000000000000000000000000004m, Read(35, -29)); // 3.5e-28 to the even 4
        Assert.Equal(0.0000000000000000000000000003m, Read(251, -30)); // over the half: up
        Assert.Equal(0m, Read(1, -40)); // under a decimal's smallest step: zero
        Assert.Equal(79228162514264337593543950335m, Read(UInt128.Parse("79228162514264337593543950335"), 0));
        Assert.Equal(7922816251426433759354395034m, Read(UInt128.Parse("792281625142643375935439503355"), -2)); // ….3355 rounds to ….336, past 2^96: one digit more, from the exact value
        Assert.Throws<OverflowException>(() => Read(UInt128.Parse("792281625142643375935439503355"), -1)); // ….335.5 rounds past the largest decimal
        Assert.Equal(1000m, Read(1, 3)); // a positive exponent
        Assert.Throws<OverflowException>(() => Read(1, 29));
        Assert.Throws<OverflowException>(() => Read(UInt128.Parse("79228162514264337593543950336"), 0));
    }

    [Fact]
    public void Decimal128_refuses_nan_and_infinity()
    {
        var nan = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(nan.AsSpan(8), 0x7C00000000000000);
        Assert.Throws<OverflowException>(() => Decimal128.Read(nan));
        var infinity = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(infinity.AsSpan(8), 0x7800000000000000);
        Assert.Throws<OverflowException>(() => Decimal128.Read(infinity));
    }

    private static decimal Read(UInt128 coefficient, int exponent)
    {
        var bits = coefficient | ((UInt128)(uint)(exponent + 6176) << 113);
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, (ulong)bits);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), (ulong)(bits >> 64));
        return Decimal128.Read(bytes);
    }
}
