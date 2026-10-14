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
}
