// Follows libwebp (BSD-3-Clause, Copyright (c) 2010 Google Inc.): see THIRD-PARTY-NOTICES.md.
namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     VP8's loop filter (RFC 6386, section 15), simple and normal, over one plane in place. The edge
///     tests and taps are libwebp's <c>dsp/dec.c</c>, which compare <c>4|p0 - q0| + |p1 - q1|</c> against
///     <c>2t + 1</c>: the specification's <c>2|p0 - q0| + |p1 - q1| / 2 &lt;= t</c> without the halving.
/// </summary>
internal static class Vp8LoopFilter
{
    /// <summary>The simple filter across one edge of 16 pixels: <paramref name="step" /> crosses the edge, <paramref name="along" /> runs along it.</summary>
    public static void SimpleEdge(byte[] p, int offset, int step, int along, int threshold)
    {
        var limit = 2 * threshold + 1;
        for (var i = 0; i < 16; i++)
        {
            var at = offset + i * along;
            if (NeedsFilter(p, at, step, limit))
                Filter2(p, at, step);
        }
    }

    /// <summary>The normal filter across a macroblock edge: six taps, or two where the edge has high variance.</summary>
    public static void MacroblockEdge(byte[] p, int offset, int step, int along, int size, int threshold, int interior, int hevThreshold)
    {
        var limit = 2 * threshold + 1;
        for (var i = 0; i < size; i++)
        {
            var at = offset + i * along;
            if (!NeedsFilter2(p, at, step, limit, interior))
                continue;
            if (HighEdgeVariance(p, at, step, hevThreshold))
                Filter2(p, at, step);
            else
                Filter6(p, at, step);
        }
    }

    /// <summary>The normal filter across an inner edge: four taps, or two where the edge has high variance.</summary>
    public static void InnerEdge(byte[] p, int offset, int step, int along, int size, int threshold, int interior, int hevThreshold)
    {
        var limit = 2 * threshold + 1;
        for (var i = 0; i < size; i++)
        {
            var at = offset + i * along;
            if (!NeedsFilter2(p, at, step, limit, interior))
                continue;
            if (HighEdgeVariance(p, at, step, hevThreshold))
                Filter2(p, at, step);
            else
                Filter4(p, at, step);
        }
    }

    private static bool NeedsFilter(byte[] p, int at, int step, int limit)
    {
        int p1 = p[at - 2 * step], p0 = p[at - step], q0 = p[at], q1 = p[at + step];
        return 4 * Math.Abs(p0 - q0) + Math.Abs(p1 - q1) <= limit;
    }

    private static bool NeedsFilter2(byte[] p, int at, int step, int limit, int interior)
    {
        int p3 = p[at - 4 * step], p2 = p[at - 3 * step], p1 = p[at - 2 * step], p0 = p[at - step];
        int q0 = p[at], q1 = p[at + step], q2 = p[at + 2 * step], q3 = p[at + 3 * step];
        if (4 * Math.Abs(p0 - q0) + Math.Abs(p1 - q1) > limit)
            return false;
        return Math.Abs(p3 - p2) <= interior && Math.Abs(p2 - p1) <= interior && Math.Abs(p1 - p0) <= interior &&
               Math.Abs(q3 - q2) <= interior && Math.Abs(q2 - q1) <= interior && Math.Abs(q1 - q0) <= interior;
    }

    private static bool HighEdgeVariance(byte[] p, int at, int step, int threshold)
    {
        int p1 = p[at - 2 * step], p0 = p[at - step], q0 = p[at], q1 = p[at + step];
        return Math.Abs(p1 - p0) > threshold || Math.Abs(q1 - q0) > threshold;
    }

    private static void Filter2(byte[] p, int at, int step)
    {
        int p1 = p[at - 2 * step], p0 = p[at - step], q0 = p[at], q1 = p[at + step];
        var a = 3 * (q0 - p0) + SClip1(p1 - q1);
        var a1 = SClip2((a + 4) >> 3);
        var a2 = SClip2((a + 3) >> 3);
        p[at - step] = Clip(p0 + a2);
        p[at] = Clip(q0 - a1);
    }

    private static void Filter4(byte[] p, int at, int step)
    {
        int p1 = p[at - 2 * step], p0 = p[at - step], q0 = p[at], q1 = p[at + step];
        var a = 3 * (q0 - p0);
        var a1 = SClip2((a + 4) >> 3);
        var a2 = SClip2((a + 3) >> 3);
        var a3 = (a1 + 1) >> 1;
        p[at - 2 * step] = Clip(p1 + a3);
        p[at - step] = Clip(p0 + a2);
        p[at] = Clip(q0 - a1);
        p[at + step] = Clip(q1 - a3);
    }

    private static void Filter6(byte[] p, int at, int step)
    {
        int p2 = p[at - 3 * step], p1 = p[at - 2 * step], p0 = p[at - step];
        int q0 = p[at], q1 = p[at + step], q2 = p[at + 2 * step];
        var a = SClip1(3 * (q0 - p0) + SClip1(p1 - q1));
        var a1 = (27 * a + 63) >> 7;
        var a2 = (18 * a + 63) >> 7;
        var a3 = (9 * a + 63) >> 7;
        p[at - 3 * step] = Clip(p2 + a3);
        p[at - 2 * step] = Clip(p1 + a2);
        p[at - step] = Clip(p0 + a1);
        p[at] = Clip(q0 - a1);
        p[at + step] = Clip(q1 - a2);
        p[at + 2 * step] = Clip(q2 - a3);
    }

    private static int SClip1(int v) => v < -128 ? -128 : v > 127 ? 127 : v;

    private static int SClip2(int v) => v < -16 ? -16 : v > 15 ? 15 : v;

    private static byte Clip(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
}
