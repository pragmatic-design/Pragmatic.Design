// Follows libwebp (BSD-3-Clause, Copyright (c) 2010 Google Inc.): see THIRD-PARTY-NOTICES.md.
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     What coding costs, in 1/256 of a bit: a bit at a given probability, an intra mode, and a coefficient
///     level at each position and context under the frame's current coefficient probabilities. The encoder's
///     mode decision weighs these against distortion. The tables come from the shared array pool; dispose it to
///     give them back.
/// </summary>
internal sealed class Vp8Costs : IDisposable
{
    /// <summary>The level past which a coefficient's adaptive part costs the same (category 6's first node on).</summary>
    public const int MaxVariableLevel = 67;

    private const int Levels = MaxVariableLevel + 1;
    private const int Positions = Vp8Tables.NumTypes * 16 * Vp8Tables.NumContexts;

    private static readonly ushort[] Entropy = BuildEntropy();
    private static readonly ushort[] FixedLevelCosts = BuildFixedLevelCosts();
    private static readonly int[] ExcessFixedCosts = BuildExcessFixedCosts();
    private static readonly int[] SubblockModeCosts = BuildSubblockModeCosts();

    // The tables under the default probabilities, filled by the static constructor once Entropy and
    // FixedLevelCosts, which they are built from, are.
    private static readonly ushort[] DefaultLevelCosts = new ushort[Positions * Levels];
    private static readonly ushort[] DefaultEndCosts = new ushort[Positions];
    private static readonly ushort[] DefaultNotEndCosts = new ushort[Positions];

    static Vp8Costs() => Compute(Vp8Tables.DefaultCoefficientProbabilities, DefaultLevelCosts, DefaultEndCosts, DefaultNotEndCosts);

    // [type][position 0..15][context][level 0..67], the band of each position resolved.
    private readonly ushort[] _levelCosts = ArrayPool<ushort>.Shared.Rent(Positions * Levels);

    // [type][position][context]: the end-of-block bit coded at that position, and its opposite.
    private readonly ushort[] _endCosts = ArrayPool<ushort>.Shared.Rent(Positions);
    private readonly ushort[] _notEndCosts = ArrayPool<ushort>.Shared.Rent(Positions);

    // The probabilities the tables were computed from; none yet, so the first update computes.
    private readonly byte[] _probabilities = new byte[Vp8Tables.DefaultCoefficientProbabilities.Length];
    private bool _computed;

    public void Dispose()
    {
        ArrayPool<ushort>.Shared.Return(_levelCosts);
        ArrayPool<ushort>.Shared.Return(_endCosts);
        ArrayPool<ushort>.Shared.Return(_notEndCosts);
    }

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

    /// <summary>
    ///     Recomputes the level costs from the coefficient probabilities [type, band, context, node], unless
    ///     they are the ones the tables already hold, as libwebp skips a refresh that changes nothing.
    /// </summary>
    public void Update(byte[] probabilities)
    {
        if (_computed && probabilities.AsSpan().SequenceEqual(_probabilities))
            return;
        Compute(probabilities, _levelCosts, _endCosts, _notEndCosts);
        probabilities.CopyTo(_probabilities, 0);
        _computed = true;
    }

    /// <summary>
    ///     The level costs under the default coefficient probabilities, which every frame starts from: computed
    ///     once, and copied.
    /// </summary>
    public void UseDefaults()
    {
        DefaultLevelCosts.CopyTo(_levelCosts, 0);
        DefaultEndCosts.CopyTo(_endCosts, 0);
        DefaultNotEndCosts.CopyTo(_notEndCosts, 0);
        Vp8Tables.DefaultCoefficientProbabilities.CopyTo(_probabilities);
        _computed = true;
    }

    private static void Compute(ReadOnlySpan<byte> probabilities, Span<ushort> levelCosts, Span<ushort> endCosts, Span<ushort> notEndCosts)
    {
        var bands = Vp8Tables.Bands;
        for (var type = 0; type < Vp8Tables.NumTypes; type++)
        {
            for (var n = 0; n < 16; n++)
            {
                // The probabilities are per band, and eight positions share band 6: a position whose band an
                // earlier one has takes that one's tables, as libwebp maps its positions to its band tables.
                var earlier = bands[..n].IndexOf(bands[n]);
                if (earlier >= 0)
                {
                    const int c = Vp8Tables.NumContexts;
                    var from = (type * 16 + earlier) * c;
                    var to = (type * 16 + n) * c;
                    endCosts.Slice(from, c).CopyTo(endCosts.Slice(to, c));
                    notEndCosts.Slice(from, c).CopyTo(notEndCosts.Slice(to, c));
                    levelCosts.Slice(from * Levels, c * Levels).CopyTo(levelCosts.Slice(to * Levels, c * Levels));
                    continue;
                }

                for (var ctx = 0; ctx < Vp8Tables.NumContexts; ctx++)
                {
                    var p = Vp8Tokens.Offset(type, bands[n], ctx);
                    var position = (type * 16 + n) * Vp8Tables.NumContexts + ctx;
                    endCosts[position] = (ushort)Bit(0, probabilities[p]);
                    notEndCosts[position] = (ushort)Bit(1, probabilities[p]);
                    var table = levelCosts.Slice(position * Levels, Levels);
                    var notEnd = ctx > 0 ? Bit(1, probabilities[p]) : 0;
                    table[0] = (ushort)(Bit(0, probabilities[p + 1]) + notEnd);
                    var nonZero = Bit(1, probabilities[p + 1]) + notEnd;
                    for (var v = 1; v < Levels; v++)
                        table[v] = (ushort)(nonZero + VariableCost(v, probabilities.Slice(p, Vp8Tables.NumProbabilities)) + FixedLevelCosts[v]);
                }
            }
        }
    }

    /// <summary>
    ///     The cost of one block's levels (scan order) of <paramref name="type" />, from position
    ///     <paramref name="first" />, in context <paramref name="ctx0" /> (libwebp's <c>GetResidualCost</c>).
    /// </summary>
    public int Residual(int type, int first, int ctx0, ReadOnlySpan<short> levels) => Residual(type, first, ctx0, levels, out _);

    /// <summary>
    ///     The same, and whether any level from <paramref name="first" /> on is non-zero: the context the block
    ///     gives its neighbours, found on the way rather than by scanning the levels again.
    /// </summary>
    [SkipLocalsInit]
    public int Residual(int type, int first, int ctx0, ReadOnlySpan<short> levels, out bool coded)
    {
        if (levels.Length < 16)
            throw new ArgumentException("A block has 16 levels.");

        ref var values = ref MemoryMarshal.GetReference(levels);
        var zero = Vector128<short>.Zero;
        var zeros = Vector128.Equals(Vector128.LoadUnsafe(ref values), zero).ExtractMostSignificantBits() |
                    (Vector128.Equals(Vector128.LoadUnsafe(ref values, 8), zero).ExtractMostSignificantBits() << 8);
        var nonZero = ~zeros & (0xFFFFu << first) & 0xFFFFu;
        coded = nonZero != 0;
        var start = (type * 16 + first) * Vp8Tables.NumContexts + ctx0;
        if (nonZero == 0)
            return _endCosts[start];

        var last = 31 - BitOperations.LeadingZeroCount(nonZero);

        // The not-the-end bit of the first coefficient is in the tables only for a context above zero.
        var cost = ctx0 == 0 ? _notEndCosts[start] : 0;

        // The absolute levels, their clamp to the tables and the contexts they make, sixteen at once, as
        // libwebp's GetResidualCost_SSE2 does: the loop below then takes no branch on a level. On a photograph
        // the levels vary enough that those branches, mispredicted, cost more than the lookups.
        var low = Vector128.Min(Vector128.Abs(Vector128.LoadUnsafe(ref values)), Vector128.Create((short)Vp8QuantMatrix.MaxLevel));
        var high = Vector128.Min(Vector128.Abs(Vector128.LoadUnsafe(ref values, 8)), Vector128.Create((short)Vp8QuantMatrix.MaxLevel));
        var variable = Vector128.Create((short)MaxVariableLevel);
        var clamped = Vector128.Narrow(Vector128.Min(low, variable).AsUInt16(), Vector128.Min(high, variable).AsUInt16());
        Span<byte> clamps = stackalloc byte[16];
        Span<byte> contexts = stackalloc byte[16];
        Span<ushort> absolutes = stackalloc ushort[16];
        clamped.CopyTo(clamps);
        Vector128.Min(clamped, Vector128.Create((byte)2)).CopyTo(contexts);
        low.AsUInt16().CopyTo(absolutes);
        high.AsUInt16().CopyTo(absolutes[8..]);
        ref var clamp = ref MemoryMarshal.GetReference(clamps);
        ref var context = ref MemoryMarshal.GetReference(contexts);
        ref var absolute = ref MemoryMarshal.GetReference(absolutes);
        ref var excess = ref MemoryMarshal.GetArrayDataReference(ExcessFixedCosts);

        // The tables hold the fixed bits of a level too, up to the last level whose adaptive part varies; a
        // larger level adds what its fixed bits cost beyond those.
        ref var table = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_levelCosts), type * 16 * Vp8Tables.NumContexts * Levels);
        var row = (first * Vp8Tables.NumContexts + ctx0) * Levels;
        var n = first;
        for (; n < last; n++)
        {
            cost += Unsafe.Add(ref table, row + Unsafe.Add(ref clamp, n)) + Unsafe.Add(ref excess, Unsafe.Add(ref absolute, n));
            row = ((n + 1) * Vp8Tables.NumContexts + Unsafe.Add(ref context, n)) * Levels;
        }

        cost += Unsafe.Add(ref table, row + Unsafe.Add(ref clamp, n)) + Unsafe.Add(ref excess, Unsafe.Add(ref absolute, n));
        if (last < 15)
            cost += _endCosts[(type * 16 + last + 1) * Vp8Tables.NumContexts + (Unsafe.Add(ref absolute, n) == 1 ? 1 : 2)];
        return cost;
    }

    /// <summary>What the fixed bits of a level past <see cref="MaxVariableLevel" /> cost beyond those of that level; zero up to it.</summary>
    private static int[] BuildExcessFixedCosts()
    {
        var table = new int[Vp8QuantMatrix.MaxLevel + 1];
        for (var v = MaxVariableLevel + 1; v <= Vp8QuantMatrix.MaxLevel; v++)
            table[v] = FixedLevelCosts[v] - FixedLevelCosts[MaxVariableLevel];
        return table;
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
