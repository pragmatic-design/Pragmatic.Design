using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     How far a reconstruction is from its source, over blocks in work buffers of
///     <see cref="Vp8Prediction.Stride" />, and the flatness tests that steer the mode choice away from
///     predicting a flat block with a complex mode.
/// </summary>
internal static class Vp8Distortion
{
    private const int S = Vp8Prediction.Stride;

    public static int SumOfSquares(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int offset, int width, int height)
    {
        if (Vector128.IsHardwareAccelerated && width is 8 or 16)
            return SumOfSquaresWide(a, b, offset, width, height);

        var sum = 0;
        for (var y = 0; y < height; y++)
        {
            var rowA = a.Slice(offset + y * S, width);
            var rowB = b.Slice(offset + y * S, width);
            for (var x = 0; x < rowA.Length; x++)
            {
                var d = rowA[x] - rowB[x];
                sum += d * d;
            }
        }

        return sum;
    }

    /// <summary>Rows of 8 or 16 pixels, a row at a time.</summary>
    private static int SumOfSquaresWide(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int offset, int width, int height)
    {
        var end = offset + (height - 1) * S + 16;
        if (a.Length < end || b.Length < end)
            throw new ArgumentException("The block reaches past its buffer.");

        ref var rowA = ref Unsafe.Add(ref MemoryMarshal.GetReference(a), offset);
        ref var rowB = ref Unsafe.Add(ref MemoryMarshal.GetReference(b), offset);
        var sum = Vector128<int>.Zero;
        for (var y = 0; y < height; y++)
        {
            var pixelsA = Vector128.LoadUnsafe(ref rowA, (nuint)(y * S));
            var pixelsB = Vector128.LoadUnsafe(ref rowB, (nuint)(y * S));
            var low = Vector128.WidenLower(pixelsA).AsInt16() - Vector128.WidenLower(pixelsB).AsInt16();
            sum += Vector128.WidenLower(low) * Vector128.WidenLower(low) + Vector128.WidenUpper(low) * Vector128.WidenUpper(low);
            if (width == 16)
            {
                var high = Vector128.WidenUpper(pixelsA).AsInt16() - Vector128.WidenUpper(pixelsB).AsInt16();
                sum += Vector128.WidenLower(high) * Vector128.WidenLower(high) + Vector128.WidenUpper(high) * Vector128.WidenUpper(high);
            }
        }

        return Vector128.Sum(sum);
    }

    /// <summary>
    ///     The sum of squared differences of the 8 x 8 U block and the 8 x 8 V block together: a row of each in
    ///     one vector, as libwebp's side-by-side chroma is measured in one pass.
    /// </summary>
    public static int SumOfSquaresChroma(byte[] sourceU, byte[] reconstructionU, byte[] sourceV, byte[] reconstructionV, int offset)
    {
        if (!Sse2.IsSupported)
            return SumOfSquares(sourceU, reconstructionU, offset, 8, 8) + SumOfSquares(sourceV, reconstructionV, offset, 8, 8);

        var end = offset + 7 * S + 8;
        if (sourceU.Length < end || reconstructionU.Length < end || sourceV.Length < end || reconstructionV.Length < end)
            throw new ArgumentException("The blocks reach past their buffers.");

        ref var su = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(sourceU), offset);
        ref var ru = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(reconstructionU), offset);
        ref var sv = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(sourceV), offset);
        ref var rv = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(reconstructionV), offset);
        var sum = Vector128<int>.Zero;
        for (var y = 0; y < 8 * S; y += S)
        {
            var a = Vector128.Create(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref su, y)), Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref sv, y))).AsByte();
            var b = Vector128.Create(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref ru, y)), Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref rv, y))).AsByte();
            var low = Vector128.WidenLower(a).AsInt16() - Vector128.WidenLower(b).AsInt16();
            var high = Vector128.WidenUpper(a).AsInt16() - Vector128.WidenUpper(b).AsInt16();
            sum += Sse2.MultiplyAddAdjacent(low, low) + Sse2.MultiplyAddAdjacent(high, high);
        }

        return Vector128.Sum(sum);
    }

    /// <summary>The sum of squared differences of a 4 x 4 block, sixteen pixels at once where vectors are accelerated.</summary>
    public static int SumOfSquares4(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int offset)
    {
        if (!Vector128.IsHardwareAccelerated)
            return SumOfSquares(a, b, offset, 4, 4);
        if (a.Length < offset + 3 * S + 4 || b.Length < offset + 3 * S + 4)
            throw new ArgumentException("The block reaches past its buffer.");

        var rowsA = Rows(ref Unsafe.Add(ref MemoryMarshal.GetReference(a), offset));
        var rowsB = Rows(ref Unsafe.Add(ref MemoryMarshal.GetReference(b), offset));
        var low = Vector128.WidenLower(rowsA).AsInt16() - Vector128.WidenLower(rowsB).AsInt16();
        var high = Vector128.WidenUpper(rowsA).AsInt16() - Vector128.WidenUpper(rowsB).AsInt16();
        var lowSquares = Vector128.WidenLower(low) * Vector128.WidenLower(low) + Vector128.WidenUpper(low) * Vector128.WidenUpper(low);
        var highSquares = Vector128.WidenLower(high) * Vector128.WidenLower(high) + Vector128.WidenUpper(high) * Vector128.WidenUpper(high);
        return Vector128.Sum(lowSquares + highSquares);
    }

    /// <summary>The four rows of four bytes of a block in a work buffer, one after the other.</summary>
    private static Vector128<byte> Rows(ref byte block) =>
        Vector128.Create(
            Unsafe.ReadUnaligned<uint>(ref block),
            Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref block, S)),
            Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref block, 2 * S)),
            Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref block, 3 * S))).AsByte();

    /// <summary>How many of a block's 15 AC levels (scan order, after the DC) are not zero.</summary>
    public static int NonZeroAc(ReadOnlySpan<short> levels)
    {
        if (levels.Length < 16)
            throw new ArgumentException("A block has 16 levels.");
        ref var values = ref MemoryMarshal.GetReference(levels);
        var zero = Vector128<short>.Zero;
        var zeros = Vector128.Equals(Vector128.LoadUnsafe(ref values), zero).ExtractMostSignificantBits() |
                    (Vector128.Equals(Vector128.LoadUnsafe(ref values, 8), zero).ExtractMostSignificantBits() << 8);
        return BitOperations.PopCount(~zeros & 0xFFFE);
    }

    /// <summary>Whether a 16 x 16 source block is one value throughout.</summary>
    public static bool IsFlatSource(ReadOnlySpan<byte> source, int offset)
    {
        var value = source[offset];
        for (var y = 0; y < 16; y++)
        {
            if (source.Slice(offset + y * S, 16).ContainsAnyExcept(value))
                return false;
        }

        return true;
    }

    /// <summary>Whether <paramref name="blocks" /> blocks of levels have <paramref name="threshold" /> non-zero AC levels or fewer.</summary>
    public static bool IsFlat(ReadOnlySpan<short> levels, int blocks, int threshold)
    {
        var count = 0;
        for (var b = 0; b < blocks; b++)
        {
            count += NonZeroAc(levels.Slice(b * 16, 16));
            if (count > threshold)
                return false;
        }

        return true;
    }
}
