// Follows libwebp (BSD-3-Clause, Copyright (c) 2010 Google Inc.): see THIRD-PARTY-NOTICES.md.
namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     How hard the loop filter works on one macroblock: its edge limit (0 for none), interior limit and
///     high-variance threshold, and whether its inner edges are filtered too.
/// </summary>
internal readonly record struct Vp8FilterStrength(int Limit, int Interior, int HighVarianceThreshold, bool Inner)
{
    /// <summary>
    ///     The strengths for each segment, without and with subblock prediction (libwebp's
    ///     <c>PrecomputeFilterStrengths</c>): <c>[segment * 2 + (subblocks ? 1 : 0)]</c>.
    /// </summary>
    public static Vp8FilterStrength[] ForSegments(Vp8FrameHeader header)
    {
        var strengths = new Vp8FilterStrength[Vp8FrameHeader.Segments * 2];
        for (var segment = 0; segment < Vp8FrameHeader.Segments; segment++)
        {
            var baseLevel = header.FilterLevel;
            if (header.UseSegments)
            {
                baseLevel = header.SegmentFilterStrength[segment];
                if (!header.AbsoluteSegmentValues)
                    baseLevel += header.FilterLevel;
            }

            for (var subblocks = 0; subblocks <= 1; subblocks++)
            {
                var level = baseLevel;
                if (header.UseFilterDeltas)
                {
                    level += header.ReferenceFilterDelta[0];
                    if (subblocks == 1)
                        level += header.ModeFilterDelta[0];
                }

                level = Math.Clamp(level, 0, 63);
                if (level == 0)
                {
                    strengths[segment * 2 + subblocks] = new Vp8FilterStrength(0, 0, 0, subblocks == 1);
                    continue;
                }

                var interior = level;
                if (header.Sharpness > 0)
                {
                    interior >>= header.Sharpness > 4 ? 2 : 1;
                    interior = Math.Min(interior, 9 - header.Sharpness);
                }

                interior = Math.Max(interior, 1);
                var threshold = level >= 40 ? 2 : level >= 15 ? 1 : 0;
                strengths[segment * 2 + subblocks] = new Vp8FilterStrength(2 * level + interior, interior, threshold, subblocks == 1);
            }
        }

        return strengths;
    }
}
