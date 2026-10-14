// Copyright 2018 Ulf Adams
// Ported to C# for Pragmatic.Design from ryu/d2s.c (d2d, d2d_small_int) at
// https://github.com/ulfjack/ryu/tree/4c0618b0e44f7ef027ebae05d2cc7812048f7c8f.
//
// Used under the Boost Software License, Version 1.0.
//    (See the accompanying LICENSE-Boost.txt or copy at https://www.boost.org/LICENSE_1_0.txt)
//
// Unless required by applicable law or agreed to in writing, this software
// is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.

namespace Pragmatic.Serialization.Ryu;

/// <summary>The shortest decimal that reads back as a given double: Ryu (Ulf Adams, PLDI 2018).</summary>
internal static class RyuDouble
{
    private const int MantissaBits = 52;
    private const int ExponentBits = 11;
    private const int Bias = 1023;

    /// <summary>
    ///     The digits and the decimal exponent of a finite, non-zero <paramref name="bits" />, sign ignored:
    ///     <c>|value| = digits × 10^exponent</c>, the digits as few as round-trip, the closest of those to the value.
    /// </summary>
    public static ulong ShortestDecimal(ulong bits, out int exponent)
    {
        var ieeeMantissa = bits & ((1UL << MantissaBits) - 1);
        var ieeeExponent = (uint)((bits >> MantissaBits) & ((1u << ExponentBits) - 1));

        return SmallInteger(ieeeMantissa, ieeeExponent, out exponent) is var integer and not 0
            ? integer
            : Decimal(ieeeMantissa, ieeeExponent, out exponent);
    }

    // An integer in [1, 2^53) is its own shortest decimal; the zeros it ends with move into the exponent. 0 means
    // the value is not one.
    private static ulong SmallInteger(ulong ieeeMantissa, uint ieeeExponent, out int exponent)
    {
        exponent = 0;
        var m2 = (1UL << MantissaBits) | ieeeMantissa;
        var e2 = (int)ieeeExponent - Bias - MantissaBits;

        if (e2 > 0 || e2 < -52)
            return 0;

        var mask = (1UL << -e2) - 1;
        if ((m2 & mask) != 0)
            return 0;

        var value = m2 >> -e2;
        while (true)
        {
            var q = value / 10;
            if (value - 10 * q != 0)
                break;

            value = q;
            exponent++;
        }

        return value;
    }

    private static ulong Decimal(ulong ieeeMantissa, uint ieeeExponent, out int exponent)
    {
        int e2;
        ulong m2;
        if (ieeeExponent == 0)
        {
            // Two more bits, so the bounds computation has room.
            e2 = 1 - Bias - MantissaBits - 2;
            m2 = ieeeMantissa;
        }
        else
        {
            e2 = (int)ieeeExponent - Bias - MantissaBits - 2;
            m2 = (1UL << MantissaBits) | ieeeMantissa;
        }

        var acceptBounds = (m2 & 1) == 0;

        // The interval of decimals that read back as this value: (mm, mp) around mv, all times 2^e2.
        var mv = 4 * m2;
        var mmShift = ieeeMantissa != 0 || ieeeExponent <= 1 ? 1u : 0u;

        ulong vr, vp, vm;
        int e10;
        var vmIsTrailingZeros = false;
        var vrIsTrailingZeros = false;
        if (e2 >= 0)
        {
            var q = RyuMath.Log10Pow2(e2) - (e2 > 3 ? 1u : 0u);
            e10 = (int)q;
            var k = RyuTables.Pow5InvBitCount + RyuMath.Pow5Bits((int)q) - 1;
            var i = -e2 + (int)q + k;
            var low = RyuTables.Pow5InvSplit[(int)q * 2];
            var high = RyuTables.Pow5InvSplit[(int)q * 2 + 1];
            vr = RyuMath.MulShift64(mv, low, high, i);
            vp = RyuMath.MulShift64(mv + 2, low, high, i);
            vm = RyuMath.MulShift64(mv - 1 - mmShift, low, high, i);

            if (q <= 21)
            {
                // Only one of mp, mv and mm can be a multiple of 5, if any.
                if (mv % 5 == 0)
                    vrIsTrailingZeros = RyuMath.MultipleOfPowerOf5(mv, q);
                else if (acceptBounds)
                    vmIsTrailingZeros = RyuMath.MultipleOfPowerOf5(mv - 1 - mmShift, q);
                else if (RyuMath.MultipleOfPowerOf5(mv + 2, q))
                    vp--;
            }
        }
        else
        {
            var q = RyuMath.Log10Pow5(-e2) - (-e2 > 1 ? 1u : 0u);
            e10 = (int)q + e2;
            var i = -e2 - (int)q;
            var k = RyuMath.Pow5Bits(i) - RyuTables.Pow5BitCount;
            var j = (int)q - k;
            var low = RyuTables.Pow5Split[i * 2];
            var high = RyuTables.Pow5Split[i * 2 + 1];
            vr = RyuMath.MulShift64(mv, low, high, j);
            vp = RyuMath.MulShift64(mv + 2, low, high, j);
            vm = RyuMath.MulShift64(mv - 1 - mmShift, low, high, j);

            if (q <= 1)
            {
                // mv = 4 * m2 always has two trailing zero bits; mm has one exactly when mmShift is 1; mp has one.
                vrIsTrailingZeros = true;
                if (acceptBounds)
                    vmIsTrailingZeros = mmShift == 1;
                else
                    vp--;
            }
            else if (q < 63)
            {
                vrIsTrailingZeros = RyuMath.MultipleOfPowerOf2(mv, q);
            }
        }

        var removed = 0;
        ulong output;
        if (vmIsTrailingZeros || vrIsTrailingZeros)
        {
            // The general case, rare (about 0.7% of doubles).
            var lastRemovedDigit = 0u;
            while (true)
            {
                var vpDiv10 = vp / 10;
                var vmDiv10 = vm / 10;
                if (vpDiv10 <= vmDiv10)
                    break;

                var vmMod10 = (uint)(vm - 10 * vmDiv10);
                var vrDiv10 = vr / 10;
                var vrMod10 = (uint)(vr - 10 * vrDiv10);
                vmIsTrailingZeros &= vmMod10 == 0;
                vrIsTrailingZeros &= lastRemovedDigit == 0;
                lastRemovedDigit = vrMod10;
                vr = vrDiv10;
                vp = vpDiv10;
                vm = vmDiv10;
                removed++;
            }

            if (vmIsTrailingZeros)
            {
                while (true)
                {
                    var vmDiv10 = vm / 10;
                    if (vm - 10 * vmDiv10 != 0)
                        break;

                    var vrDiv10 = vr / 10;
                    var vrMod10 = (uint)(vr - 10 * vrDiv10);
                    vrIsTrailingZeros &= lastRemovedDigit == 0;
                    lastRemovedDigit = vrMod10;
                    vr = vrDiv10;
                    vp /= 10;
                    vm = vmDiv10;
                    removed++;
                }
            }

            // Exactly halfway (...50..0): round to even.
            if (vrIsTrailingZeros && lastRemovedDigit == 5 && vr % 2 == 0)
                lastRemovedDigit = 4;

            // vr + 1 when vr is outside the interval or the removed digits round up.
            output = vr + ((vr == vm && (!acceptBounds || !vmIsTrailingZeros)) || lastRemovedDigit >= 5 ? 1UL : 0UL);
        }
        else
        {
            // The common case, specialized.
            var roundUp = false;
            var vpDiv100 = vp / 100;
            var vmDiv100 = vm / 100;
            if (vpDiv100 > vmDiv100)
            {
                // Two digits at a time first: most doubles lose at least two.
                var vrDiv100 = vr / 100;
                var vrMod100 = (uint)(vr - 100 * vrDiv100);
                roundUp = vrMod100 >= 50;
                vr = vrDiv100;
                vp = vpDiv100;
                vm = vmDiv100;
                removed += 2;
            }

            while (true)
            {
                var vpDiv10 = vp / 10;
                var vmDiv10 = vm / 10;
                if (vpDiv10 <= vmDiv10)
                    break;

                var vrDiv10 = vr / 10;
                var vrMod10 = (uint)(vr - 10 * vrDiv10);
                roundUp = vrMod10 >= 5;
                vr = vrDiv10;
                vp = vpDiv10;
                vm = vmDiv10;
                removed++;
            }

            output = vr + (vr == vm || roundUp ? 1UL : 0UL);
        }

        exponent = e10 + removed;
        return output;
    }
}
