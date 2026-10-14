// Copyright 2018 Ulf Adams
// Ported to C# for Pragmatic.Design from ryu/f2s.c (f2d) at
// https://github.com/ulfjack/ryu/tree/4c0618b0e44f7ef027ebae05d2cc7812048f7c8f.
//
// Used under the Boost Software License, Version 1.0.
//    (See the accompanying LICENSE-Boost.txt or copy at https://www.boost.org/LICENSE_1_0.txt)
//
// Unless required by applicable law or agreed to in writing, this software
// is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.

namespace Pragmatic.Serialization.Ryu;

/// <summary>The shortest decimal that reads back as a given float: Ryu (Ulf Adams, PLDI 2018).</summary>
internal static class RyuFloat
{
    private const int MantissaBits = 23;
    private const int ExponentBits = 8;
    private const int Bias = 127;

    // The float path reads the double tables: their high word, and the bit counts less 64.
    private const int Pow5InvBitCount = RyuTables.Pow5InvBitCount - 64;
    private const int Pow5BitCount = RyuTables.Pow5BitCount - 64;

    /// <summary>
    ///     The digits and the decimal exponent of a finite, non-zero <paramref name="bits" />, sign ignored:
    ///     <c>|value| = digits × 10^exponent</c>, the digits as few as round-trip, the closest of those to the value.
    /// </summary>
    public static uint ShortestDecimal(uint bits, out int exponent)
    {
        var ieeeMantissa = bits & ((1u << MantissaBits) - 1);
        var ieeeExponent = (bits >> MantissaBits) & ((1u << ExponentBits) - 1);

        int e2;
        uint m2;
        if (ieeeExponent == 0)
        {
            // Two more bits, so the bounds computation has room.
            e2 = 1 - Bias - MantissaBits - 2;
            m2 = ieeeMantissa;
        }
        else
        {
            e2 = (int)ieeeExponent - Bias - MantissaBits - 2;
            m2 = (1u << MantissaBits) | ieeeMantissa;
        }

        var acceptBounds = (m2 & 1) == 0;

        // The interval of decimals that read back as this value: (mm, mp) around mv, all times 2^e2.
        var mv = 4 * m2;
        var mp = 4 * m2 + 2;
        var mmShift = ieeeMantissa != 0 || ieeeExponent <= 1 ? 1u : 0u;
        var mm = 4 * m2 - 1 - mmShift;

        uint vr, vp, vm;
        int e10;
        var vmIsTrailingZeros = false;
        var vrIsTrailingZeros = false;
        var lastRemovedDigit = 0u;
        if (e2 >= 0)
        {
            var q = RyuMath.Log10Pow2(e2);
            e10 = (int)q;
            var k = Pow5InvBitCount + RyuMath.Pow5Bits((int)q) - 1;
            var i = -e2 + (int)q + k;
            vr = MulPow5InvDivPow2(mv, q, i);
            vp = MulPow5InvDivPow2(mp, q, i);
            vm = MulPow5InvDivPow2(mm, q, i);

            if (q != 0 && (vp - 1) / 10 <= vm / 10)
            {
                // One removed digit is needed even when the loop below removes none. q - 1 above would need a 33-bit
                // result.
                var l = Pow5InvBitCount + RyuMath.Pow5Bits((int)(q - 1)) - 1;
                lastRemovedDigit = MulPow5InvDivPow2(mv, q - 1, -e2 + (int)q - 1 + l) % 10;
            }

            if (q <= 9)
            {
                // Only one of mp, mv and mm can be a multiple of 5, if any.
                if (mv % 5 == 0)
                    vrIsTrailingZeros = RyuMath.MultipleOfPowerOf5(mv, q);
                else if (acceptBounds)
                    vmIsTrailingZeros = RyuMath.MultipleOfPowerOf5(mm, q);
                else if (RyuMath.MultipleOfPowerOf5(mp, q))
                    vp--;
            }
        }
        else
        {
            var q = RyuMath.Log10Pow5(-e2);
            e10 = (int)q + e2;
            var i = -e2 - (int)q;
            var k = RyuMath.Pow5Bits(i) - Pow5BitCount;
            var j = (int)q - k;
            vr = MulPow5DivPow2(mv, i, j);
            vp = MulPow5DivPow2(mp, i, j);
            vm = MulPow5DivPow2(mm, i, j);

            if (q != 0 && (vp - 1) / 10 <= vm / 10)
            {
                j = (int)q - 1 - (RyuMath.Pow5Bits(i + 1) - Pow5BitCount);
                lastRemovedDigit = MulPow5DivPow2(mv, i + 1, j) % 10;
            }

            if (q <= 1)
            {
                // mv = 4 * m2 always has two trailing zero bits; mm has one exactly when mmShift is 1; mp has one.
                vrIsTrailingZeros = true;
                if (acceptBounds)
                    vmIsTrailingZeros = mmShift == 1;
                else
                    vp--;
            }
            else if (q < 31)
            {
                vrIsTrailingZeros = (mv & ((1u << (int)(q - 1)) - 1)) == 0;
            }
        }

        var removed = 0;
        uint output;
        if (vmIsTrailingZeros || vrIsTrailingZeros)
        {
            // The general case, rare (about 4% of floats).
            while (vp / 10 > vm / 10)
            {
                vmIsTrailingZeros &= vm % 10 == 0;
                vrIsTrailingZeros &= lastRemovedDigit == 0;
                lastRemovedDigit = vr % 10;
                vr /= 10;
                vp /= 10;
                vm /= 10;
                removed++;
            }

            if (vmIsTrailingZeros)
            {
                while (vm % 10 == 0)
                {
                    vrIsTrailingZeros &= lastRemovedDigit == 0;
                    lastRemovedDigit = vr % 10;
                    vr /= 10;
                    vp /= 10;
                    vm /= 10;
                    removed++;
                }
            }

            // Exactly halfway (...50..0): round to even.
            if (vrIsTrailingZeros && lastRemovedDigit == 5 && vr % 2 == 0)
                lastRemovedDigit = 4;

            // vr + 1 when vr is outside the interval or the removed digits round up.
            output = vr + ((vr == vm && (!acceptBounds || !vmIsTrailingZeros)) || lastRemovedDigit >= 5 ? 1u : 0u);
        }
        else
        {
            // The common case, specialized.
            while (vp / 10 > vm / 10)
            {
                lastRemovedDigit = vr % 10;
                vr /= 10;
                vp /= 10;
                vm /= 10;
                removed++;
            }

            output = vr + (vr == vm || lastRemovedDigit >= 5 ? 1u : 0u);
        }

        exponent = e10 + removed;
        return output;
    }

    // The inverse multipliers are [2^x / 5^y] + 1; the high word of the double table is [2^x / 5^y], so the 1 is
    // added here. The 1 already in the table's low word never carries into the high one.
    private static uint MulPow5InvDivPow2(uint m, uint q, int j)
        => RyuMath.MulShift32(m, RyuTables.Pow5InvSplit[(int)q * 2 + 1] + 1, j);

    private static uint MulPow5DivPow2(uint m, int i, int j)
        => RyuMath.MulShift32(m, RyuTables.Pow5Split[i * 2 + 1], j);
}
