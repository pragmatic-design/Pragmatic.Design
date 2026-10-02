using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Xlsx;

/// <summary>
/// Renders a <see cref="SpreadsheetModel"/> to XLSX format (OOXML).
/// Zero external dependencies — uses ZipArchive + XmlWriter.
/// </summary>
public static class XlsxRenderer
{
    /// <summary>Render to a byte array.</summary>
    public static byte[] Render(SpreadsheetModel model)
    {
        using var ms = new MemoryStream();
        RenderTo(ms, model);
        return ms.ToArray();
    }

    /// <summary>Render to a stream.</summary>
    public static void RenderTo(Stream stream, SpreadsheetModel model)
    {
        Internal.XlsxPackageWriter.Build(stream, model);
    }

    /// <summary>Render to a byte array (async wrapper).</summary>
    public static Task<byte[]> RenderAsync(SpreadsheetModel model, CancellationToken ct = default)
        => Task.Run(() => Render(model), ct);

    /// <summary>Render to a stream (async wrapper).</summary>
    public static Task RenderToStreamAsync(Stream stream, SpreadsheetModel model, CancellationToken ct = default)
        => Task.Run(() => RenderTo(stream, model), ct);
}
