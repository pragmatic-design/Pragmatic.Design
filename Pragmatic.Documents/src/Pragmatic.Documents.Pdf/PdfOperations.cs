using System.Runtime.InteropServices;

namespace Pragmatic.Documents.Pdf;

/// <summary>
/// Static one-liner helpers for common PDF operations: merge, split, page count.
/// </summary>
public static class PdfOperations
{
    /// <summary>Merge multiple PDFs into a single PDF.</summary>
    public static byte[] Merge(params ReadOnlySpan<byte[]> pdfs)
    {
        if (pdfs.Length == 0) throw new ArgumentException("At least one PDF is required.", nameof(pdfs));
        if (pdfs.Length == 1) return pdfs[0];

        try { return MergeCore(pdfs); }
        catch (DllNotFoundException ex) { throw NativePdfImports.NativeUnavailable(ex); }
        catch (BadImageFormatException ex) { throw NativePdfImports.NativeUnavailable(ex); }
    }

    /// <summary>Merge multiple PDFs into a single PDF asynchronously.</summary>
    public static Task<byte[]> MergeAsync(byte[][] pdfs, CancellationToken ct = default)
        => Task.Run(() => Merge(pdfs), ct);

    /// <summary>Extract a range of pages from a PDF (1-based, inclusive).</summary>
    public static byte[] Split(ReadOnlySpan<byte> pdf, uint fromPage, uint toPage)
    {
        try { return SplitCore(pdf, fromPage, toPage); }
        catch (DllNotFoundException ex) { throw NativePdfImports.NativeUnavailable(ex); }
        catch (BadImageFormatException ex) { throw NativePdfImports.NativeUnavailable(ex); }
    }

    /// <summary>Extract a range of pages from a PDF asynchronously.</summary>
    public static Task<byte[]> SplitAsync(ReadOnlyMemory<byte> pdf, uint fromPage, uint toPage, CancellationToken ct = default)
        => Task.Run(() => Split(pdf.Span, fromPage, toPage), ct);

    /// <summary>Get the number of pages in a PDF.</summary>
    public static uint GetPageCount(ReadOnlySpan<byte> pdf)
    {
        try
        {
            unsafe
            {
                uint count;
                fixed (byte* ptr = pdf)
                {
                    var rc = NativePdfImports.pragmatic_pdf_page_count(ptr, (nuint)pdf.Length, &count);
                    if (rc < 0)
                        ThrowNativeError("page_count", rc);
                }
                return count;
            }
        }
        catch (DllNotFoundException ex) { throw NativePdfImports.NativeUnavailable(ex); }
        catch (BadImageFormatException ex) { throw NativePdfImports.NativeUnavailable(ex); }
    }

    private static unsafe byte[] MergeCore(ReadOnlySpan<byte[]> pdfs)
    {
        var count = pdfs.Length;

        // Pin all PDF byte arrays and collect their pointers + lengths
        var handles = new GCHandle[count];
        var ptrs = stackalloc nint[count];
        var lens = stackalloc nuint[count];

        try
        {
            for (var i = 0; i < count; i++)
            {
                handles[i] = GCHandle.Alloc(pdfs[i], GCHandleType.Pinned);
                ptrs[i] = handles[i].AddrOfPinnedObject();
                lens[i] = (nuint)pdfs[i].Length;
            }

            nint outPdf;
            nuint outLen;

            var rc = NativePdfImports.pragmatic_pdf_merge(
                ptrs, lens, (nuint)count,
                &outPdf, &outLen);

            if (rc < 0)
                ThrowNativeError("merge", rc);

            // Always free the native buffer, even if the length check or copy throws.
            try
            {
                var len = CheckedLength(outLen, "merge");
                var result = new byte[len];
                Marshal.Copy(outPdf, result, 0, len);
                return result;
            }
            finally
            {
                NativePdfImports.pragmatic_free_buffer(outPdf, outLen);
            }
        }
        finally
        {
            for (var i = 0; i < count; i++)
            {
                if (handles[i].IsAllocated)
                    handles[i].Free();
            }
        }
    }

    private static unsafe byte[] SplitCore(ReadOnlySpan<byte> pdf, uint fromPage, uint toPage)
    {
        nint outPdf;
        nuint outLen;

        fixed (byte* ptr = pdf)
        {
            var rc = NativePdfImports.pragmatic_pdf_split(
                ptr, (nuint)pdf.Length,
                fromPage, toPage,
                &outPdf, &outLen);

            if (rc < 0)
                ThrowNativeError("split", rc);
        }

        // Ensure native buffer is always freed even if Marshal.Copy throws (e.g. OOM).
        try
        {
            var len = CheckedLength(outLen, "split");
            var result = new byte[len];
            Marshal.Copy(outPdf, result, 0, len);
            return result;
        }
        finally
        {
            NativePdfImports.pragmatic_free_buffer(outPdf, outLen);
        }
    }

    /// <summary>
    /// Validates a native byte length fits in an <see cref="int"/> before sizing a managed array
    /// / driving <see cref="Marshal.Copy(nint, byte[], int, int)"/>. Output above 2 GB would wrap
    /// on a raw (int) cast, so we throw a clear error instead of silently truncating.
    /// </summary>
    private static int CheckedLength(nuint length, string operation)
    {
        if (length > (nuint)int.MaxValue)
            throw new PdfRenderException(
                $"PDF {operation} output is {length} bytes, which exceeds the {int.MaxValue}-byte limit for a managed buffer.");
        return (int)length;
    }

    private static void ThrowNativeError(string operation, int rc)
    {
        var buf = Marshal.AllocHGlobal(4096);
        try
        {
            var written = NativePdfImports.pragmatic_last_error(buf, 4096);
            var msg = written > 0
                ? Marshal.PtrToStringUTF8(buf) ?? $"{operation} failed with code {rc}"
                : $"{operation} failed with code {rc}";
            throw new PdfRenderException(msg);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }
}
