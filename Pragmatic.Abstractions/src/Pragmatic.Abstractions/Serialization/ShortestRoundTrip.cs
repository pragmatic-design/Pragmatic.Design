using System.Runtime.CompilerServices;
using Pragmatic.Serialization.Ryu;

namespace Pragmatic.Serialization;

/// <summary>
///     A finite double or float in UTF-8, byte for byte what <c>TryFormat(destination, out _, provider:
///     CultureInfo.InvariantCulture)</c> writes, with the digits found by Ryu instead of .NET's Grisu and Dragon4 (#149).
/// </summary>
/// <remarks>
///     The layout is .NET's general format at round-trip precision (<c>Number.Formatting.cs</c>, release/10.0): the
///     shortest digits that read back, fixed notation unless the decimal point would sit more than 17 places (9 for a
///     float) to the right of the first digit or more than 3 zeros to its left, and then <c>E</c> with a sign and at
///     least two digits of exponent. Negative zero is <c>-0</c>. The tests compare it with <c>TryFormat</c> over every
///     float and a seeded set of doubles.
/// </remarks>
internal static class ShortestRoundTrip
{
    private const int DoubleMaxDigits = 17;
    private const int FloatMaxDigits = 9;

    /// <summary>Writes a finite <paramref name="value" />; <paramref name="destination" /> has room for 25 bytes.</summary>
    public static int Format(double value, Span<byte> destination)
    {
        var bits = BitConverter.DoubleToUInt64Bits(value);
        var negative = (long)bits < 0;
        if ((bits << 1) == 0)
            return Zero(negative, destination);

        var digits = RyuDouble.ShortestDecimal(bits, out var exponent);
        return Layout(negative, digits, exponent, DoubleMaxDigits, destination);
    }

    /// <summary>Writes a finite <paramref name="value" />; <paramref name="destination" /> has room for 16 bytes.</summary>
    public static int Format(float value, Span<byte> destination)
    {
        var bits = BitConverter.SingleToUInt32Bits(value);
        var negative = (int)bits < 0;
        if ((bits << 1) == 0)
            return Zero(negative, destination);

        var digits = RyuFloat.ShortestDecimal(bits, out var exponent);
        return Layout(negative, digits, exponent, FloatMaxDigits, destination);
    }

    private static int Zero(bool negative, Span<byte> destination)
    {
        if (!negative)
        {
            destination[0] = (byte)'0';
            return 1;
        }

        destination[0] = (byte)'-';
        destination[1] = (byte)'0';
        return 2;
    }

    private static int Layout(bool negative, ulong digits, int exponent, int maxDigits, Span<byte> destination)
    {
        // .NET's digits never end in zero: Ryu's can when they round up into one, or come from an integer.
        while (true)
        {
            var q = digits / 10;
            if (digits - 10 * q != 0)
                break;

            digits = q;
            exponent++;
        }

        var at = 0;
        if (negative)
            destination[at++] = (byte)'-';

        var length = RyuMath.DecimalLength17(digits);

        // value = 0.d1d2…dn × 10^scale, the number .NET decides on.
        var scale = exponent + length;

        if (scale > maxDigits || scale < -3)
        {
            // d1.d2…dnE±XX: the first digit moves one to the left, over the place left for it.
            WriteDigits(digits, destination.Slice(at + 1, length));
            destination[at] = destination[at + 1];
            if (length > 1)
            {
                destination[at + 1] = (byte)'.';
                at += length + 1;
            }
            else
            {
                at++;
            }

            return at + Exponent(scale - 1, destination[at..]);
        }

        if (scale >= length)
        {
            // An integer: the digits, then the zeros up to the decimal point.
            WriteDigits(digits, destination.Slice(at, length));
            at += length;
            destination.Slice(at, scale - length).Fill((byte)'0');
            return at + scale - length;
        }

        if (scale > 0)
        {
            // The point inside the digits: the integer part moves one to the left, over the place left for it.
            WriteDigits(digits, destination.Slice(at + 1, length));
            destination.Slice(at + 1, scale).CopyTo(destination[at..]);
            destination[at + scale] = (byte)'.';
            return at + length + 1;
        }

        // 0.000d1…dn, at most three zeros after the point.
        destination[at++] = (byte)'0';
        destination[at++] = (byte)'.';
        destination.Slice(at, -scale).Fill((byte)'0');
        at -= scale;
        WriteDigits(digits, destination.Slice(at, length));
        return at + length;
    }

    // E, a sign always, and at least two digits.
    private static int Exponent(int exponent, Span<byte> destination)
    {
        destination[0] = (byte)'E';
        destination[1] = exponent < 0 ? (byte)'-' : (byte)'+';
        var magnitude = (uint)Math.Abs(exponent);
        var pairs = RyuTables.DigitPairs;
        if (magnitude >= 100)
        {
            var hundreds = magnitude / 100;
            var rest = (int)(magnitude - 100 * hundreds) * 2;
            destination[2] = (byte)('0' + hundreds);
            destination[3] = pairs[rest];
            destination[4] = pairs[rest + 1];
            return 5;
        }

        var pair = (int)magnitude * 2;
        destination[2] = pairs[pair];
        destination[3] = pairs[pair + 1];
        return 4;
    }

    // Exactly destination.Length digits of value, two at a time from the right.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteDigits(ulong value, Span<byte> destination)
    {
        var pairs = RyuTables.DigitPairs;
        var at = destination.Length;

        if ((value >> 32) != 0)
        {
            // Eight digits off with one 64-bit division; the rest fits 32 bits.
            var q = value / 100000000;
            var low = (uint)(value - 100000000 * q);
            value = q;
            for (var n = 0; n < 4; n++)
            {
                var c = low % 100;
                low /= 100;
                at -= 2;
                destination[at] = pairs[(int)c * 2];
                destination[at + 1] = pairs[(int)c * 2 + 1];
            }
        }

        var rest = (uint)value;
        while (rest >= 100)
        {
            var c = rest % 100;
            rest /= 100;
            at -= 2;
            destination[at] = pairs[(int)c * 2];
            destination[at + 1] = pairs[(int)c * 2 + 1];
        }

        if (rest >= 10)
        {
            destination[at - 2] = pairs[(int)rest * 2];
            destination[at - 1] = pairs[(int)rest * 2 + 1];
        }
        else
        {
            destination[at - 1] = (byte)('0' + rest);
        }
    }
}
