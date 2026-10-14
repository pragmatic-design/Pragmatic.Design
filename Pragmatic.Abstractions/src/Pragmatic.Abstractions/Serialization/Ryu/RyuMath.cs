// Copyright 2018 Ulf Adams
// Ported to C# for Pragmatic.Design from ryu/common.h, ryu/d2s_intrinsics.h and ryu/f2s_intrinsics.h at
// https://github.com/ulfjack/ryu/tree/4c0618b0e44f7ef027ebae05d2cc7812048f7c8f.
//
// Used under the Boost Software License, Version 1.0.
//    (See the accompanying LICENSE-Boost.txt or copy at https://www.boost.org/LICENSE_1_0.txt)
//
// Unless required by applicable law or agreed to in writing, this software
// is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.

using System.Runtime.CompilerServices;

namespace Pragmatic.Serialization.Ryu;

/// <summary>The integer arithmetic Ryu's double and float paths share.</summary>
internal static class RyuMath
{
    /// <summary>e == 0 ? 1 : ceil(log2(5^e)), for 0 &lt;= e &lt;= 3528.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Pow5Bits(int e) => (int)(((uint)e * 1217359) >> 19) + 1;

    /// <summary>floor(log10(2^e)), for 0 &lt;= e &lt;= 1650.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Log10Pow2(int e) => ((uint)e * 78913) >> 18;

    /// <summary>floor(log10(5^e)), for 0 &lt;= e &lt;= 2620.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Log10Pow5(int e) => ((uint)e * 732923) >> 20;

    /// <summary>The number of digits of <paramref name="v" />, which has at most 17.</summary>
    public static int DecimalLength17(ulong v)
    {
        if (v >= 10000000000000000UL) return 17;
        if (v >= 1000000000000000UL) return 16;
        if (v >= 100000000000000UL) return 15;
        if (v >= 10000000000000UL) return 14;
        if (v >= 1000000000000UL) return 13;
        if (v >= 100000000000UL) return 12;
        if (v >= 10000000000UL) return 11;
        if (v >= 1000000000UL) return 10;
        return DecimalLength9((uint)v);
    }

    /// <summary>The number of digits of <paramref name="v" />, which has at most 9.</summary>
    public static int DecimalLength9(uint v)
    {
        if (v >= 100000000) return 9;
        if (v >= 10000000) return 8;
        if (v >= 1000000) return 7;
        if (v >= 100000) return 6;
        if (v >= 10000) return 5;
        if (v >= 1000) return 4;
        if (v >= 100) return 3;
        if (v >= 10) return 2;
        return 1;
    }

    /// <summary>Whether <paramref name="value" /> is divisible by 5^<paramref name="p" />.</summary>
    public static bool MultipleOfPowerOf5(ulong value, uint p)
    {
        // 5 * inverse = 1 (mod 2^64): multiplying a multiple of 5 by it divides exactly, and lands at or under 2^64 / 5.
        const ulong inverse = 14757395258967641293UL;
        const ulong limit = 3689348814741910323UL;

        var count = 0u;
        while (true)
        {
            value *= inverse;
            if (value > limit)
                break;
            count++;
        }

        return count >= p;
    }

    /// <summary>Whether <paramref name="value" /> is divisible by 5^<paramref name="p" />.</summary>
    public static bool MultipleOfPowerOf5(uint value, uint p)
    {
        var count = 0u;
        while (value % 5 == 0)
        {
            value /= 5;
            count++;
        }

        return count >= p;
    }

    /// <summary>Whether <paramref name="value" /> is divisible by 2^<paramref name="p" />, for p &lt; 64.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool MultipleOfPowerOf2(ulong value, uint p) => (value & ((1UL << (int)p) - 1)) == 0;

    /// <summary>
    ///     The 55-bit <paramref name="m" /> times a 128-bit multiplier from a table, shifted right by
    ///     <paramref name="j" />: the top 64 bits of the product are all a shift of at least 115 can leave.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong MulShift64(ulong m, ulong mulLow, ulong mulHigh, int j)
    {
        var high1 = Math.BigMul(m, mulHigh, out var low1);
        var high0 = Math.BigMul(m, mulLow, out _);
        var sum = high0 + low1;
        if (sum < high0)
            high1++;

        // 0 < j - 64 < 64 for every exponent a double has.
        var shift = j - 64;
        return (high1 << (64 - shift)) | (sum >> shift);
    }

    /// <summary>The 32-bit <paramref name="m" /> times a 64-bit <paramref name="factor" />, shifted right by <paramref name="shift" /> (more than 32).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint MulShift32(uint m, ulong factor, int shift)
    {
        var bits0 = (ulong)m * (uint)factor;
        var bits1 = (ulong)m * (uint)(factor >> 32);
        var sum = (bits0 >> 32) + bits1;
        return (uint)(sum >> (shift - 32));
    }
}
