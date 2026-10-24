using System.Buffers.Binary;

namespace Pragmatic.Persistence.Documents.Mongo.Bson;

/// <summary>
///     BSON's decimal128 (IEEE 754-2008, binary integer decimal) to and from <see cref="decimal" />. A decimal is a 96-bit coefficient and a
///     scale 0-28, so it always fits: coefficient as it is, exponent = -scale. The way back keeps the value exactly when a decimal can hold it,
///     rounds half to even when the decimal128 has more digits than a decimal keeps (29), and throws <see cref="OverflowException" /> beyond a
///     decimal's range or for NaN and the infinities.
/// </summary>
public static class Decimal128
{
    private const int ExponentBias = 6176;
    private static readonly UInt128 CoefficientMask = (UInt128.One << 113) - 1;
    private static readonly UInt128 MaxCanonicalCoefficient = UInt128.Parse("9999999999999999999999999999999999", System.Globalization.CultureInfo.InvariantCulture); // 10^34 - 1
    private static readonly UInt128 MaxDecimalCoefficient = (UInt128.One << 96) - 1;

    /// <summary>Writes the 16 bytes (little-endian, as BSON stores them) of <paramref name="value" />.</summary>
    public static void Write(Span<byte> destination, decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        var coefficient = new UInt128((uint)bits[2], ((ulong)(uint)bits[1] << 32) | (uint)bits[0]);
        var scale = (bits[3] >> 16) & 0xFF;
        var negative = bits[3] < 0;
        var encoded = coefficient | ((UInt128)(uint)(ExponentBias - scale) << 113);
        if (negative)
            encoded |= UInt128.One << 127;
        BinaryPrimitives.WriteUInt64LittleEndian(destination, (ulong)encoded);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], (ulong)(encoded >> 64));
    }

    /// <summary>Reads 16 bytes (little-endian) as a decimal.</summary>
    public static decimal Read(ReadOnlySpan<byte> source)
    {
        var low = BinaryPrimitives.ReadUInt64LittleEndian(source);
        var high = BinaryPrimitives.ReadUInt64LittleEndian(source[8..]);
        var negative = (high >> 63) != 0;
        UInt128 coefficient;
        int exponent;
        if (((high >> 61) & 0b11) == 0b11)
        {
            // The combination field starts 11: NaN (11111), an infinity (11110), or the large-coefficient form, whose coefficient (over 10^34) is
            // never canonical and reads as zero.
            var combination = (high >> 58) & 0x1F;
            if (combination == 0x1F)
                throw new OverflowException("The decimal128 is NaN: a decimal has none.");
            if (combination == 0x1E)
                throw new OverflowException("The decimal128 is an infinity: a decimal has none.");
            exponent = (int)((high >> 47) & 0x3FFF) - ExponentBias;
            coefficient = UInt128.Zero;
        }
        else
        {
            exponent = (int)((high >> 49) & 0x3FFF) - ExponentBias;
            coefficient = new UInt128(high, low) & CoefficientMask;
            if (coefficient > MaxCanonicalCoefficient)
                coefficient = UInt128.Zero;
        }

        if (coefficient == UInt128.Zero)
            return new decimal(0, 0, 0, negative, (byte)Math.Clamp(-exponent, 0, 28));
        // A positive exponent: the coefficient times 10^exponent, while it fits.
        while (exponent > 0)
        {
            if (coefficient > MaxDecimalCoefficient / 10)
                throw new OverflowException("The decimal128 is beyond a decimal's range.");
            coefficient *= 10;
            exponent--;
        }

        // Too many digits or too small a scale for a decimal: the fewest digits off the coefficient that make it fit, the quotient rounded half to
        // even on the exact remainder; when the rounding carries past the largest coefficient, one digit more (from the exact value, not twice rounded).
        var drop = 0;
        while (drop < 35 && (exponent + drop < -28 || coefficient / Pow10(drop) > MaxDecimalCoefficient))
            drop++; // 10^35 would no longer fit the arithmetic, and from 35 digits on nothing of a coefficient under 10^34 is left
        for (; ; drop++)
        {
            if (drop >= 35)
                return new decimal(0, 0, 0, negative, 28); // the coefficient is under 10^34: every digit dropped, the value rounds to zero
            if (exponent + drop > 0)
                throw new OverflowException("The decimal128 is beyond a decimal's range.");
            var divisor = Pow10(drop);
            var quotient = coefficient / divisor;
            var remainder = coefficient % divisor;
            var half = divisor / 2;
            if (drop > 0 && (remainder > half || (remainder == half && (quotient & UInt128.One) == UInt128.One)))
                quotient++;
            if (quotient > MaxDecimalCoefficient)
                continue;
            var lower = (ulong)quotient;
            return new decimal((int)(uint)lower, (int)(uint)(lower >> 32), (int)(uint)(ulong)(quotient >> 64), negative, (byte)-(exponent + drop));
        }
    }

    private static UInt128 Pow10(int exponent)
    {
        var value = UInt128.One;
        for (var i = 0; i < exponent; i++)
            value *= 10;
        return value;
    }
}
