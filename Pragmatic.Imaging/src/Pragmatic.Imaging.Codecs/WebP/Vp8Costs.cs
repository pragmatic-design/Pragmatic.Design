using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     What coding costs, in 1/256 of a bit: a bit at a given probability, an intra mode, and a coefficient
///     level at each position and context under the frame's current coefficient probabilities. The encoder's
///     mode decision weighs these against distortion.
/// </summary>
internal sealed class Vp8Costs
{
    /// <summary>The level past which a coefficient's adaptive part costs the same (category 6's first node on).</summary>
    public const int MaxVariableLevel = 67;

    private const int Levels = MaxVariableLevel + 1;

    private static readonly ushort[] Entropy = BuildEntropy();
    private static readonly ushort[] FixedLevelCosts = BuildFixedLevelCosts();
    private static readonly int[] SubblockModeCosts = BuildSubblockModeCosts();

    // [type][position 0..15][context][level 0..67], the band of each position resolved.
    private readonly ushort[] _levelCosts = new ushort[Vp8Tables.NumTypes * 16 * Vp8Tables.NumContexts * Levels];

    // [type][position][context]: the end-of-block bit coded at that position, and its opposite.
    private readonly ushort[] _endCosts = new ushort[Vp8Tables.NumTypes * 16 * Vp8Tables.NumContexts];
    private readonly ushort[] _notEndCosts = new ushort[Vp8Tables.NumTypes * 16 * Vp8Tables.NumContexts];

    /// <summary>The cost of coding <paramref name="bit" /> at <paramref name="probability" /> (of a zero, out of 256).</summary>
    public static int Bit(int bit, int probability) => Entropy[bit != 0 ? 256 - probability : probability];

    /// <summary>The cost of a 16 x 16 luma mode, the bit that says it is one included.</summary>
    public static int Luma16Mode(int mode) => Bit(1, 145) + mode switch
    {
        Vp8Modes.Dc => Bit(0, 156) + Bit(0, 163),
        Vp8Modes.V => Bit(0, 156) + Bit(1, 163),
        Vp8Modes.H => Bit(1, 156) + Bit(0, 128),
        _ => Bit(1, 156) + Bit(1, 128),
    };

    /// <summary>The cost of the bit that says the luma is predicted in 4 x 4 blocks.</summary>
    public static int SubblocksFlag => Bit(0, 145);

    /// <summary>The cost of a 4 x 4 mode given the modes above and to the left of its block.</summary>
    public static int SubblockMode(int above, int left, int mode) => SubblockModeCosts[(above * 10 + left) * 10 + mode];

    public static int ChromaMode(int mode) => mode switch
    {
        Vp8Modes.Dc => Bit(0, 142),
        Vp8Modes.V => Bit(1, 142) + Bit(0, 114),
        Vp8Modes.H => Bit(1, 142) + Bit(1, 114) + Bit(0, 183),
        _ => Bit(1, 142) + Bit(1, 114) + Bit(1, 183),
    };

    /// <summary>Recomputes the level costs from the coefficient probabilities [type, band, context, node].</summary>
    public void Update(byte[] probabilities)
    {
        var bands = Vp8Tables.Bands;
        for (var type = 0; type < Vp8Tables.NumTypes; type++)
        {
            for (var n = 0; n < 16; n++)
            {
                for (var ctx = 0; ctx < Vp8Tables.NumContexts; ctx++)
                {
                    var p = Vp8Tokens.Offset(type, bands[n], ctx);
                    var position = (type * 16 + n) * Vp8Tables.NumContexts + ctx;
                    _endCosts[position] = (ushort)Bit(0, probabilities[p]);
                    _notEndCosts[position] = (ushort)Bit(1, probabilities[p]);
                    var table = _levelCosts.AsSpan(position * Levels, Levels);
                    var notEnd = ctx > 0 ? Bit(1, probabilities[p]) : 0;
                    table[0] = (ushort)(Bit(0, probabilities[p + 1]) + notEnd);
                    var nonZero = Bit(1, probabilities[p + 1]) + notEnd;
                    for (var v = 1; v < Levels; v++)
                        table[v] = (ushort)(nonZero + VariableCost(v, probabilities.AsSpan(p, Vp8Tables.NumProbabilities)) + FixedLevelCosts[v]);
                }
            }
        }
    }

    /// <summary>
    ///     The cost of one block's levels (scan order) of <paramref name="type" />, from position
    ///     <paramref name="first" />, in context <paramref name="ctx0" /> (libwebp's <c>GetResidualCost</c>).
    /// </summary>
    public int Residual(int type, int first, int ctx0, ReadOnlySpan<short> levels)
    {
        if (levels.Length < 16)
            throw new ArgumentException("A block has 16 levels.");

        ref var values = ref MemoryMarshal.GetReference(levels);
        var zero = Vector128<short>.Zero;
        var zeros = Vector128.Equals(Vector128.LoadUnsafe(ref values), zero).ExtractMostSignificantBits() |
                    (Vector128.Equals(Vector128.LoadUnsafe(ref values, 8), zero).ExtractMostSignificantBits() << 8);
        var nonZero = ~zeros & (0xFFFFu << first) & 0xFFFFu;
        var start = (type * 16 + first) * Vp8Tables.NumContexts + ctx0;
        if (nonZero == 0)
            return _endCosts[start];

        var last = 31 - BitOperations.LeadingZeroCount(nonZero);

        // The not-the-end bit of the first coefficient is in the tables only for a context above zero.
        var cost = ctx0 == 0 ? _notEndCosts[start] : 0;

        // The tables hold the fixed bits of a level too, up to the last level whose adaptive part varies.
        ref var table = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_levelCosts), type * 16 * Vp8Tables.NumContexts * Levels);
        var row = (first * Vp8Tables.NumContexts + ctx0) * Levels;
        int v;
        for (var n = first; ; n++)
        {
            v = Unsafe.Add(ref values, n);
            if (v < 0)
                v = -v;
            cost += v <= MaxVariableLevel
                ? Unsafe.Add(ref table, row + v)
                : Unsafe.Add(ref table, row + MaxVariableLevel) + FixedLevelCosts[Math.Min(v, Vp8QuantMatrix.MaxLevel)] - FixedLevelCosts[MaxVariableLevel];
            if (n == last)
                break;
            row = ((n + 1) * Vp8Tables.NumContexts + (v >= 2 ? 2 : v)) * Levels;
        }

        if (last < 15)
            cost += _endCosts[(type * 16 + last + 1) * Vp8Tables.NumContexts + (v == 1 ? 1 : 2)];
        return cost;
    }

    /// <summary>The cost of the adaptive nodes after the first two that code <paramref name="level" />.</summary>
    private static int VariableCost(int level, ReadOnlySpan<byte> p)
    {
        if (level == 1)
            return Bit(0, p[2]);
        var cost = Bit(1, p[2]);
        if (level <= 4)
        {
            cost += Bit(0, p[3]);
            if (level == 2)
                return cost + Bit(0, p[4]);
            return cost + Bit(1, p[4]) + Bit(level == 4 ? 1 : 0, p[5]);
        }

        cost += Bit(1, p[3]);
        if (level <= 10)
            return cost + Bit(0, p[6]) + Bit(level > 6 ? 1 : 0, p[7]);

        cost += Bit(1, p[6]);
        if (level < 3 + (8 << 1))
            return cost + Bit(0, p[8]) + Bit(0, p[9]);
        if (level < 3 + (8 << 2))
            return cost + Bit(0, p[8]) + Bit(1, p[9]);
        if (level < 3 + (8 << 3))
            return cost + Bit(1, p[8]) + Bit(0, p[10]);
        return cost + Bit(1, p[8]) + Bit(1, p[10]);
    }

    private static ushort[] BuildEntropy()
    {
        var table = new ushort[257];
        for (var k = 1; k <= 256; k++)
            table[k] = (ushort)Math.Round(-Math.Log2(k / 256.0) * 256);
        table[0] = table[1];
        return table;
    }

    /// <summary>The bits of a level coded at fixed probabilities: its sign, and the extra bits of its token.</summary>
    private static ushort[] BuildFixedLevelCosts()
    {
        var table = new ushort[Vp8QuantMatrix.MaxLevel + 1];
        for (var v = 1; v <= Vp8QuantMatrix.MaxLevel; v++)
        {
            var cost = 256; // the sign
            if (v is 5 or 6)
            {
                cost += Bit(v == 6 ? 1 : 0, 159);
            }
            else if (v is >= 7 and <= 10)
            {
                cost += Bit(v >= 9 ? 1 : 0, 165) + Bit((v & 1) == 0 ? 1 : 0, 145);
            }
            else if (v > 10)
            {
                var extra = Vp8Tables.LargeCategory(v, out _, out var bits, out var first);
                var rest = v - first;
                for (var i = 0; i < bits; i++)
                    cost += Bit((rest >> (bits - 1 - i)) & 1, extra[i]);
            }

            table[v] = (ushort)cost;
        }

        return table;
    }

    /// <summary>The cost of each 4 x 4 mode by the modes above and to the left.</summary>
    private static int[] BuildSubblockModeCosts()
    {
        var table = new int[10 * 10 * 10];
        var probabilities = Vp8Tables.BModeProbabilities;
        for (var context = 0; context < 100; context++)
        {
            for (var mode = 0; mode < 10; mode++)
                table[context * 10 + mode] = Vp8SubblockModeTree.Cost(probabilities.Slice(context * 9, 9), mode);
        }

        return table;
    }
}
