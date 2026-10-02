using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Pdf;

/// <summary>
/// Renders a <see cref="DocumentModel"/> to PDF via the Rust Typst engine.
/// </summary>
public static class PdfRenderer
{
    private static bool? _isSupported;

    /// <summary>
    /// Whether native PDF rendering is available on the current platform (the native library can be
    /// loaded). Currently true only on win-x64. Probe this to degrade gracefully instead of catching
    /// exceptions from a render call. The result is cached after the first check.
    /// </summary>
    public static bool IsSupported =>
        _isSupported ??= NativeLibrary.TryLoad(NativePdfImports.LibName, typeof(PdfRenderer).Assembly, null, out _);

    /// <summary>Render a DocumentModel to PDF bytes.</summary>
    public static byte[] Render(DocumentModel model, PdfResources? resources = null, PdfRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var effective = options ?? PdfRenderOptions.Default;
        EnsureSupported(effective);

        // Page batching: compile the document in page-bounded chunks and merge, so peak memory
        // is bounded by BatchPageCount rather than the whole document. Only worthwhile when the
        // document actually exceeds the batch size.
        if (effective.BatchPageCount > 0 && model.Pages.Count > effective.BatchPageCount)
            return RenderBatched(model, resources, effective.BatchPageCount);

        var payload = BuildPayload(model, resources);
        return RenderFromPayload(payload);
    }

    /// <summary>Render and write to a stream.</summary>
    public static void RenderTo(Stream output, DocumentModel model, PdfResources? resources = null, PdfRenderOptions? options = null)
    {
        var pdf = Render(model, resources, options);
        output.Write(pdf, 0, pdf.Length);
    }

    /// <summary>
    /// Render a DocumentModel to PDF bytes asynchronously.
    /// Note: offloads CPU-bound Typst compilation to the thread pool. Cancellation applies
    /// only before/after the compilation, not during it.
    /// </summary>
    public static Task<byte[]> RenderAsync(DocumentModel model, PdfResources? resources = null, PdfRenderOptions? options = null, CancellationToken ct = default)
    {
        return Task.Run(() => Render(model, resources, options), ct);
    }

    /// <summary>
    /// Render and write to a stream asynchronously.
    /// Note: offloads CPU-bound Typst compilation to the thread pool. Cancellation applies
    /// only before/after the compilation, not during it.
    /// </summary>
    public static async Task RenderToStreamAsync(Stream output, DocumentModel model, PdfResources? resources = null, PdfRenderOptions? options = null, CancellationToken ct = default)
    {
        var pdf = await RenderAsync(model, resources, options, ct).ConfigureAwait(false);
        await output.WriteAsync(pdf, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Guards options whose effect lives in the native Typst engine, which does not yet consume them.
    /// We fail loudly rather than accept the option and silently ignore it.
    /// </summary>
    private static void EnsureSupported(PdfRenderOptions options)
    {
        if (options.CustomFonts is { Count: > 0 })
            throw new NotSupportedException(
                "PdfRenderOptions.CustomFonts is not yet wired into the native Typst engine. " +
                "Only system fonts are available in this build.");

        if (options.MaxImageDimension > 0)
            throw new NotSupportedException(
                "PdfRenderOptions.MaxImageDimension (and the related ImageQuality) is not yet wired " +
                "into the native Typst engine. Pre-resize images before embedding them.");
    }

    private static byte[] RenderBatched(DocumentModel model, PdfResources? resources, int batchPageCount)
    {
        var batches = new List<byte[]>((model.Pages.Count + batchPageCount - 1) / batchPageCount);

        for (var start = 0; start < model.Pages.Count; start += batchPageCount)
        {
            var count = Math.Min(batchPageCount, model.Pages.Count - start);
            var pageSlice = model.Pages.Skip(start).Take(count).ToList();
            var batchModel = CloneWithPages(model, pageSlice);
            batches.Add(RenderFromPayload(BuildPayload(batchModel, resources)));
        }

        return PdfOperations.Merge(batches.ToArray());
    }

    private static DocumentModel CloneWithPages(DocumentModel source, IReadOnlyList<DocumentPage> pages) => new()
    {
        Title = source.Title,
        Author = source.Author,
        Language = source.Language,
        PageSize = source.PageSize,
        Orientation = source.Orientation,
        Margins = source.Margins,
        Pages = pages,
        Subject = source.Subject,
        Keywords = source.Keywords,
        CreatedDate = source.CreatedDate,
        Metadata = source.Metadata
    };

    private static string BuildPayload(DocumentModel model, PdfResources? resources)
    {
        var modelJson = DocumentSerializer.Serialize(model);

        if (resources is null || resources.Count == 0)
            return modelJson;

        // Inject resources into the JSON as a "resources" property
        using var doc = JsonDocument.Parse(modelJson);
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);

        writer.WriteStartObject();

        // Copy all existing properties
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            prop.WriteTo(writer);
        }

        // Add resources as base64 strings
        writer.WriteStartObject("resources");
        foreach (var (name, bytes) in resources)
        {
            writer.WriteString(name, Convert.ToBase64String(bytes));
        }
        writer.WriteEndObject();

        writer.WriteEndObject();
        writer.Flush();

        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static byte[] RenderFromPayload(string json)
    {
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

        try
        {
            unsafe
            {
                nint outPdf;
                nuint outLen;

                fixed (byte* ptr = jsonBytes)
                {
                    var rc = NativePdfImports.pragmatic_doc_to_pdf(
                        ptr, (nuint)jsonBytes.Length,
                        &outPdf, &outLen);

                    if (rc < 0)
                    {
                        var error = GetNativeError() ?? $"PDF generation failed with code {rc}";
                        throw new PdfRenderException(error);
                    }
                }

                // Ensure native buffer is always freed even if Marshal.Copy throws (e.g. OOM).
                try
                {
                    var len = CheckedLength(outLen);
                    var result = new byte[len];
                    Marshal.Copy(outPdf, result, 0, len);
                    return result;
                }
                finally
                {
                    NativePdfImports.pragmatic_free_buffer(outPdf, outLen);
                }
            }
        }
        catch (DllNotFoundException ex) { throw NativePdfImports.NativeUnavailable(ex); }
        catch (BadImageFormatException ex) { throw NativePdfImports.NativeUnavailable(ex); }
    }

    /// <summary>
    /// Validates a native byte length fits in an <see cref="int"/> before it is used to size a
    /// managed array / drive <see cref="Marshal.Copy(nint, byte[], int, int)"/>. PDFs larger than
    /// 2 GB would silently wrap on a raw (int) cast, so we fail loudly instead.
    /// </summary>
    private static int CheckedLength(nuint length)
    {
        if (length > (nuint)int.MaxValue)
            throw new PdfRenderException(
                $"PDF output is {length} bytes, which exceeds the {int.MaxValue}-byte limit for a managed buffer.");
        return (int)length;
    }

    private static string? GetNativeError()
    {
        var buf = Marshal.AllocHGlobal(4096);
        try
        {
            var written = NativePdfImports.pragmatic_last_error(buf, 4096);
            if (written <= 0) return null;
            return Marshal.PtrToStringUTF8(buf);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }
}
