// Follows libwebp (BSD-3-Clause, Copyright (c) 2010 Google Inc.): see THIRD-PARTY-NOTICES.md.
namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     Runs the loop filter over a rebuilt frame, macroblock by macroblock in raster order, each one's left
///     edge, inner vertical edges, top edge and inner horizontal edges in that order (libwebp's
///     <c>DoFilter</c>). The simple filter touches luma only.
/// </summary>
internal static class Vp8FrameFilter
{
    public static void Apply(Vp8Frame frame, Vp8FilterStrength[] strengths, bool simple)
    {
        for (var mbY = 0; mbY < frame.MacroblocksHigh; mbY++)
        {
            for (var mbX = 0; mbX < frame.MacroblocksWide; mbX++)
            {
                var strength = strengths[mbY * frame.MacroblocksWide + mbX];
                if (strength.Limit == 0)
                    continue;
                if (simple)
                    Simple(frame, mbX, mbY, strength);
                else
                    Normal(frame, mbX, mbY, strength);
            }
        }
    }

    private static void Simple(Vp8Frame frame, int mbX, int mbY, Vp8FilterStrength f)
    {
        var y = frame.Y;
        var stride = frame.YStride;
        var at = mbY * 16 * stride + mbX * 16;
        if (mbX > 0)
            Vp8LoopFilter.SimpleEdge(y, at, 1, stride, f.Limit + 4);
        if (f.Inner)
        {
            for (var k = 4; k < 16; k += 4)
                Vp8LoopFilter.SimpleEdge(y, at + k, 1, stride, f.Limit);
        }

        if (mbY > 0)
            Vp8LoopFilter.SimpleEdge(y, at, stride, 1, f.Limit + 4);
        if (f.Inner)
        {
            for (var k = 4; k < 16; k += 4)
                Vp8LoopFilter.SimpleEdge(y, at + k * stride, stride, 1, f.Limit);
        }
    }

    private static void Normal(Vp8Frame frame, int mbX, int mbY, Vp8FilterStrength f)
    {
        var stride = frame.YStride;
        var uvStride = frame.UvStride;
        var y = mbY * 16 * stride + mbX * 16;
        var uv = mbY * 8 * uvStride + mbX * 8;
        var edge = f.Limit + 4;

        if (mbX > 0)
        {
            Vp8LoopFilter.MacroblockEdge(frame.Y, y, 1, stride, 16, edge, f.Interior, f.HighVarianceThreshold);
            Vp8LoopFilter.MacroblockEdge(frame.U, uv, 1, uvStride, 8, edge, f.Interior, f.HighVarianceThreshold);
            Vp8LoopFilter.MacroblockEdge(frame.V, uv, 1, uvStride, 8, edge, f.Interior, f.HighVarianceThreshold);
        }

        if (f.Inner)
        {
            for (var k = 4; k < 16; k += 4)
                Vp8LoopFilter.InnerEdge(frame.Y, y + k, 1, stride, 16, f.Limit, f.Interior, f.HighVarianceThreshold);
            Vp8LoopFilter.InnerEdge(frame.U, uv + 4, 1, uvStride, 8, f.Limit, f.Interior, f.HighVarianceThreshold);
            Vp8LoopFilter.InnerEdge(frame.V, uv + 4, 1, uvStride, 8, f.Limit, f.Interior, f.HighVarianceThreshold);
        }

        if (mbY > 0)
        {
            Vp8LoopFilter.MacroblockEdge(frame.Y, y, stride, 1, 16, edge, f.Interior, f.HighVarianceThreshold);
            Vp8LoopFilter.MacroblockEdge(frame.U, uv, uvStride, 1, 8, edge, f.Interior, f.HighVarianceThreshold);
            Vp8LoopFilter.MacroblockEdge(frame.V, uv, uvStride, 1, 8, edge, f.Interior, f.HighVarianceThreshold);
        }

        if (f.Inner)
        {
            for (var k = 4; k < 16; k += 4)
                Vp8LoopFilter.InnerEdge(frame.Y, y + k * stride, stride, 1, 16, f.Limit, f.Interior, f.HighVarianceThreshold);
            Vp8LoopFilter.InnerEdge(frame.U, uv + 4 * uvStride, uvStride, 1, 8, f.Limit, f.Interior, f.HighVarianceThreshold);
            Vp8LoopFilter.InnerEdge(frame.V, uv + 4 * uvStride, uvStride, 1, 8, f.Limit, f.Interior, f.HighVarianceThreshold);
        }
    }
}
