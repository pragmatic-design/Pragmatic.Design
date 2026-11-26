using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The 4 x 4 transposition of four vectors of 32-bit lanes that the vectorised 4 x 4 transforms need
///     between their passes: SSE2's unpacks on x86, ARM64's zips on ARM, element moves elsewhere.
/// </summary>
internal static class Vp8Transpose
{
    /// <summary>Whether the transforms take their vectorised path: the transposition has instructions here.</summary>
    public static bool IsAccelerated => Sse2.IsSupported || AdvSimd.Arm64.IsSupported;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Apply(ref Vector128<int> r0, ref Vector128<int> r1, ref Vector128<int> r2, ref Vector128<int> r3)
    {
        if (Sse2.IsSupported)
        {
            var t0 = Sse2.UnpackLow(r0, r1);
            var t1 = Sse2.UnpackLow(r2, r3);
            var t2 = Sse2.UnpackHigh(r0, r1);
            var t3 = Sse2.UnpackHigh(r2, r3);
            r0 = Sse2.UnpackLow(t0.AsInt64(), t1.AsInt64()).AsInt32();
            r1 = Sse2.UnpackHigh(t0.AsInt64(), t1.AsInt64()).AsInt32();
            r2 = Sse2.UnpackLow(t2.AsInt64(), t3.AsInt64()).AsInt32();
            r3 = Sse2.UnpackHigh(t2.AsInt64(), t3.AsInt64()).AsInt32();
            return;
        }

        if (AdvSimd.Arm64.IsSupported)
        {
            var t0 = AdvSimd.Arm64.ZipLow(r0, r1);
            var t1 = AdvSimd.Arm64.ZipLow(r2, r3);
            var t2 = AdvSimd.Arm64.ZipHigh(r0, r1);
            var t3 = AdvSimd.Arm64.ZipHigh(r2, r3);
            r0 = AdvSimd.Arm64.ZipLow(t0.AsInt64(), t1.AsInt64()).AsInt32();
            r1 = AdvSimd.Arm64.ZipHigh(t0.AsInt64(), t1.AsInt64()).AsInt32();
            r2 = AdvSimd.Arm64.ZipLow(t2.AsInt64(), t3.AsInt64()).AsInt32();
            r3 = AdvSimd.Arm64.ZipHigh(t2.AsInt64(), t3.AsInt64()).AsInt32();
            return;
        }

        var a = r0;
        var b = r1;
        var c = r2;
        var d = r3;
        r0 = Vector128.Create(a[0], b[0], c[0], d[0]);
        r1 = Vector128.Create(a[1], b[1], c[1], d[1]);
        r2 = Vector128.Create(a[2], b[2], c[2], d[2]);
        r3 = Vector128.Create(a[3], b[3], c[3], d[3]);
    }
}
