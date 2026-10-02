using System.Runtime.InteropServices;

namespace Pragmatic.Documents.Pdf;

/// <summary>P/Invoke surface for the native <c>Pragmatic.Pdf.Native</c> library.</summary>
internal static partial class NativePdfImports
{
    internal const string LibName = "Pragmatic.Pdf.Native";

    /// <summary>
    /// Translates a native-library load failure into a clear <see cref="PdfRenderException"/>. The
    /// native Typst backend ships for win-x64 and linux-x64; elsewhere the P/Invoke throws
    /// <see cref="DllNotFoundException"/> (or <see cref="BadImageFormatException"/> on an arch mismatch)
    /// — this turns that low-level failure into an actionable message instead of a raw interop error.
    /// </summary>
    internal static PdfRenderException NativeUnavailable(Exception inner) => new(
        $"The native PDF library '{LibName}' could not be loaded on this platform " +
        $"(RID: {RuntimeInformation.RuntimeIdentifier}). Native PDF rendering ships for win-x64 and " +
        "linux-x64; see the PDF rendering documentation for platform support. Check PdfRenderer.IsSupported " +
        "to degrade gracefully.", inner);

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static unsafe partial int pragmatic_doc_to_pdf(
        byte* json, nuint json_len,
        nint* out_pdf, nuint* out_pdf_len);

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int pragmatic_last_error(nint buf, nuint buf_len);

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial void pragmatic_free_buffer(nint ptr, nuint len);

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static unsafe partial int pragmatic_pdf_merge(
        nint* pdf_ptrs, nuint* pdf_lens, nuint count,
        nint* out_pdf, nuint* out_pdf_len);

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static unsafe partial int pragmatic_pdf_split(
        byte* pdf, nuint pdf_len,
        uint from_page, uint to_page,
        nint* out_pdf, nuint* out_pdf_len);

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static unsafe partial int pragmatic_pdf_page_count(
        byte* pdf, nuint pdf_len, uint* out_count);
}
