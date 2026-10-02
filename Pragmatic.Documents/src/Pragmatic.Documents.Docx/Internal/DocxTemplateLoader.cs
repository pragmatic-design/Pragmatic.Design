using System.IO.Compression;

namespace Pragmatic.Documents.Docx.Internal;

/// <summary>
/// Extracts styles, theme, and font table from a .dotx (or .docx) template.
/// These parts are used in place of generated ones when a template is provided.
/// </summary>
internal sealed class DocxTemplateLoader
{
    internal byte[]? StylesXml { get; private init; }
    internal byte[]? ThemeXml { get; private init; }
    internal byte[]? FontTableXml { get; private init; }
    internal byte[]? NumberingXml { get; private init; }

    internal static DocxTemplateLoader Load(byte[] templateBytes)
    {
        using var ms = new MemoryStream(templateBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        return new DocxTemplateLoader
        {
            StylesXml = ExtractEntry(zip, "word/styles.xml"),
            ThemeXml = ExtractEntry(zip, "word/theme/theme1.xml"),
            FontTableXml = ExtractEntry(zip, "word/fontTable.xml"),
            NumberingXml = ExtractEntry(zip, "word/numbering.xml"),
        };
    }

    // Aligned with XlsxPackageReader.GuardEntrySize default. A legitimate DOCX template part
    // (styles.xml, theme1.xml, fontTable.xml, numbering.xml) is typically well under 1 MB;
    // 100 MB is a generous ceiling that still stops zip-bomb payloads.
    private const long MaxEntryBytes = 100L * 1024 * 1024;

    private static byte[]? ExtractEntry(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry is null) return null;

        // Reject before allocation. entry.Length is the uncompressed size recorded in the central
        // directory — a malicious template can lie about it, so we also enforce the cap during copy.
        if (entry.Length > MaxEntryBytes)
            throw new InvalidOperationException(
                $"DOCX template entry '{path}' declares {entry.Length:N0} bytes, which exceeds the {MaxEntryBytes:N0} byte limit.");

        using var stream = entry.Open();
        using var buf = new MemoryStream(capacity: (int)Math.Min(entry.Length, 64 * 1024));

        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxEntryBytes)
                throw new InvalidOperationException(
                    $"DOCX template entry '{path}' exceeds the {MaxEntryBytes:N0} byte limit during decompression.");
            buf.Write(buffer, 0, read);
        }

        return buf.ToArray();
    }
}
