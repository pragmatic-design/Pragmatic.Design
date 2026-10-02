using System.IO.Compression;
using Pragmatic.Documents.Ooxml;
using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Xlsx.Internal.Xml;

namespace Pragmatic.Documents.Xlsx.Internal;

/// <summary>Assembles the XLSX ZIP package from a SpreadsheetModel.</summary>
internal static class XlsxPackageWriter
{
    internal static void Build(Stream output, SpreadsheetModel model)
    {
        // Pre-scan: collect shared strings and styles
        var sst = new SharedStringTable();
        var styles = new StyleCollector();
        PreScan(model, sst, styles);

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        // Workbook relationships
        var wbRels = new OoxmlRelationships();
        var sheetRelIds = new List<string>();
        for (var i = 0; i < model.Sheets.Count; i++)
            sheetRelIds.Add(wbRels.Add(Ns.RelWorksheet, $"worksheets/sheet{i + 1}.xml"));

        wbRels.Add(Ns.RelStyles, "styles.xml");
        wbRels.Add(Ns.RelTheme, "theme/theme1.xml");

        var hasSharedStrings = sst.Count > 0;
        if (hasSharedStrings)
            wbRels.Add(Ns.RelSharedStrings, "sharedStrings.xml");

        // Package relationships
        var pkgRels = new OoxmlRelationships();
        pkgRels.Add(Ns.RelDocument, "xl/workbook.xml");
        pkgRels.Add(Ns.RelCoreProps, "docProps/core.xml");
        pkgRels.Add(Ns.RelAppProps, "docProps/app.xml");

        // Write parts
        WriteEntry(zip, "[Content_Types].xml",
            s => ContentTypesXmlWriter.WriteTo(s, model.Sheets.Count, hasSharedStrings));

        WriteEntry(zip, "_rels/.rels",
            s => pkgRels.WriteTo(s));

        WriteEntry(zip, "xl/workbook.xml",
            s => WorkbookXmlWriter.WriteTo(s, model, sheetRelIds));

        WriteEntry(zip, "xl/_rels/workbook.xml.rels",
            s => wbRels.WriteTo(s));

        WriteEntry(zip, "xl/styles.xml",
            s => StylesXmlWriter.WriteTo(s, styles));

        WriteEntry(zip, "xl/theme/theme1.xml",
            s => OoxmlThemeWriter.WriteTo(s, OoxmlThemeColors.Default));

        if (hasSharedStrings)
        {
            WriteEntry(zip, "xl/sharedStrings.xml",
                s => SharedStringsXmlWriter.WriteTo(s, sst));
        }

        // Worksheets
        for (var i = 0; i < model.Sheets.Count; i++)
        {
            var sheet = model.Sheets[i];
            var idx = i;
            WriteEntry(zip, $"xl/worksheets/sheet{idx + 1}.xml",
                s => WorksheetXmlWriter.WriteTo(s, sheet, sst, styles));
        }

        // Core/App properties
        var coreProps = new OoxmlCoreProperties
        {
            Title = model.Title,
            Author = model.Author
        };
        WriteEntry(zip, "docProps/core.xml",
            s => OoxmlCorePropsWriter.WriteTo(s, coreProps));
        WriteEntry(zip, "docProps/app.xml",
            s => OoxmlAppPropsWriter.WriteTo(s, "Pragmatic.Documents.Xlsx"));
    }

    /// <summary>Pre-scan model to populate shared string table and style collector.</summary>
    private static void PreScan(SpreadsheetModel model, SharedStringTable sst, StyleCollector styles)
    {
        foreach (var sheet in model.Sheets)
        foreach (var row in sheet.Rows)
        foreach (var cell in row.Cells)
        {
            if (cell.Value is string s)
                sst.GetOrAdd(s);
            // Register the style up-front for styled cells AND for date cells (which get a synthesized
            // default date number-format), so styles.xml — written before the sheets — includes them.
            if (cell.Style is not null || cell.Value is DateTime or DateTimeOffset)
                styles.GetStyleIndex(cell);
        }
    }

    private static void WriteEntry(ZipArchive zip, string path, Action<Stream> write)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        write(stream);
    }
}
