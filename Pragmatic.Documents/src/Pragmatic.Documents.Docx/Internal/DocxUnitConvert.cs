using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal;

/// <summary>Unit conversion helpers: mm/pt → OOXML units (twips, half-points, EMU).</summary>
internal static class Units
{
    /// <summary>Convert millimeters to twips (1mm ≈ 56.6929 twips).</summary>
    internal static int MmToTwips(double mm) => (int)Math.Round(mm * 56.6929);

    /// <summary>Convert points to half-points (1pt = 2 half-points). Used for w:sz.</summary>
    internal static int PtToHalfPoints(double pt) => (int)Math.Round(pt * 2);

    /// <summary>Convert millimeters to EMU (1mm = 36000 EMU). Used for images.</summary>
    internal static long MmToEmu(double mm) => (long)Math.Round(mm * 36000);

    /// <summary>Convert points to eighth-points. Used for w:spacing.</summary>
    internal static int PtToEighthPoints(double pt) => (int)Math.Round(pt * 8);

    /// <summary>Convert millimeters to twentieths of a point (same as twips). Used for spacing.</summary>
    internal static int MmToTwentiethsPt(double mm) => MmToTwips(mm);

    /// <summary>Get page size in twips for a given PageSize and orientation.</summary>
    internal static (int Width, int Height) PageSizeToTwips(PageSize size, PageOrientation orientation)
    {
        var (w, h) = size switch
        {
            PageSize.A4 => (11906, 16838),     // 210mm × 297mm
            PageSize.A3 => (16838, 23811),     // 297mm × 420mm
            PageSize.A5 => (8391, 11906),      // 148mm × 210mm
            PageSize.Letter => (12240, 15840), // 8.5" × 11"
            PageSize.Legal => (12240, 20160),  // 8.5" × 14"
            _ => (11906, 16838)
        };
        return orientation == PageOrientation.Landscape ? (h, w) : (w, h);
    }
}
