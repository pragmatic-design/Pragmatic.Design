using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Xlsx;

/// <summary>
/// Reads an XLSX file into a <see cref="SpreadsheetModel"/>.
/// Preserves formulas, cached values, merged cells, and frozen panes.
/// Note: cell styles are not currently read (style information is lost on roundtrip).
/// </summary>
public static class XlsxReader
{
    /// <summary>Read an XLSX from a byte array.</summary>
    public static SpreadsheetModel Read(byte[] data)
    {
        using var ms = new MemoryStream(data);
        return Read(ms);
    }

    /// <summary>Read an XLSX from a stream.</summary>
    public static SpreadsheetModel Read(Stream stream)
        => Internal.XlsxPackageReader.Read(stream);

    /// <summary>Read an XLSX from a byte array (async wrapper).</summary>
    public static Task<SpreadsheetModel> ReadAsync(byte[] data, CancellationToken ct = default)
        => Task.Run(() =>
        {
            // ct on Task.Run only gates scheduling; observe it inside the work so an already-
            // cancelled token short-circuits before the (potentially large) parse runs.
            ct.ThrowIfCancellationRequested();
            // MemoryStream over the existing buffer — no copy of `data`.
            using var ms = new MemoryStream(data, writable: false);
            return Read(ms);
        }, ct);

    /// <summary>Read an XLSX from a stream (async wrapper).</summary>
    public static Task<SpreadsheetModel> ReadAsync(Stream stream, CancellationToken ct = default)
        => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return Read(stream);
        }, ct);
}
