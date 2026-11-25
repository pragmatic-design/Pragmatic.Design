using System.Runtime.InteropServices;
using PDFiumCore;

// ReSharper disable once CheckNamespace
namespace Pragmatic.Testing.Visual;

/// <summary>
///     Rasterises a PDF page with PDFium, an independent renderer, so a page is compared like an image.
/// </summary>
/// <remarks>
///     The render is fixed so two renders of one page are the same image: a resolution in dots per inch, a
///     white page, annotations drawn, PDFium's default anti-aliasing for text, paths and images, and no LCD
///     text optimisation. Fonts are a property of the document: one that embeds its fonts renders the same
///     anywhere, one that does not is drawn with whatever the machine substitutes, and is not a reference
///     to compare across machines. PDFium is not thread-safe, so every call holds one lock.
/// </remarks>
public static class PdfPageRasterizer
{
    private static readonly Lock Gate = new();
    private static bool _initialised;

    public static int PageCount(byte[] pdf)
    {
        lock (Gate)
        {
            return WithDocument(pdf, fpdfview.FPDF_GetPageCount);
        }
    }

    public static RgbaImage Render(byte[] pdf, int pageIndex, int dotsPerInch)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dotsPerInch);
        lock (Gate)
        {
            return WithDocument(pdf, document => RenderPage(document, pageIndex, dotsPerInch));
        }
    }

    private static RgbaImage RenderPage(FpdfDocumentT document, int pageIndex, int dotsPerInch)
    {
        var page = fpdfview.FPDF_LoadPage(document, pageIndex)
                   ?? throw new ArgumentOutOfRangeException(nameof(pageIndex), $"PDFium cannot load page {pageIndex} (error {fpdfview.FPDF_GetLastError()}).");
        try
        {
            // Points are 1/72 inch.
            var width = (int)Math.Round(fpdfview.FPDF_GetPageWidthF(page) * dotsPerInch / 72.0);
            var height = (int)Math.Round(fpdfview.FPDF_GetPageHeightF(page) * dotsPerInch / 72.0);
            var bitmap = fpdfview.FPDFBitmapCreateEx(width, height, (int)FPDFBitmapFormat.BGRA, IntPtr.Zero, 0)
                         ?? throw new InvalidOperationException($"PDFium cannot allocate a {width} x {height} bitmap.");
            try
            {
                fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
                fpdfview.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, (int)RenderFlags.RenderAnnotations);
                return ToRgba(bitmap, width, height);
            }
            finally
            {
                fpdfview.FPDFBitmapDestroy(bitmap);
            }
        }
        finally
        {
            fpdfview.FPDF_ClosePage(page);
        }
    }

    private static RgbaImage ToRgba(FpdfBitmapT bitmap, int width, int height)
    {
        var stride = fpdfview.FPDFBitmapGetStride(bitmap);
        var buffer = fpdfview.FPDFBitmapGetBuffer(bitmap);
        var row = new byte[stride];
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(buffer + y * stride, row, 0, stride);
            for (int x = 0, i = y * width * 4; x < width; x++, i += 4)
            {
                pixels[i] = row[x * 4 + 2];
                pixels[i + 1] = row[x * 4 + 1];
                pixels[i + 2] = row[x * 4];
                pixels[i + 3] = row[x * 4 + 3];
            }
        }

        return new RgbaImage(width, height, pixels);
    }

    /// <summary>Opens the document over the bytes, pinned for as long as it is open: PDFium reads them in place.</summary>
    private static T WithDocument<T>(byte[] pdf, Func<FpdfDocumentT, T> use)
    {
        if (!_initialised)
        {
            fpdfview.FPDF_InitLibrary();
            _initialised = true;
        }

        var pinned = GCHandle.Alloc(pdf, GCHandleType.Pinned);
        try
        {
            var document = fpdfview.FPDF_LoadMemDocument(pinned.AddrOfPinnedObject(), pdf.Length, null)
                           ?? throw new ArgumentException($"PDFium cannot open the document (error {fpdfview.FPDF_GetLastError()}).", nameof(pdf));
            try
            {
                return use(document);
            }
            finally
            {
                fpdfview.FPDF_CloseDocument(document);
            }
        }
        finally
        {
            pinned.Free();
        }
    }
}
