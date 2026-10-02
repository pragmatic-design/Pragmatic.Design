using System.Globalization;
using System.Xml;
using Pragmatic.Documents.Ooxml;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Xlsx.Internal.Xml;

/// <summary>Writes xl/worksheets/sheetN.xml for a single worksheet.</summary>
internal static class WorksheetXmlWriter
{
    internal static void WriteTo(Stream stream, Sheet sheet, SharedStringTable sst, StyleCollector styles)
    {
        using var w = XmlWriter.Create(stream, OoxmlXmlSettings.Default);
        w.WriteStartDocument(true);
        w.WriteStartElement("worksheet", Ns.SS);
        w.WriteAttributeString("xmlns", "r", null, Ns.R);

        WriteSheetViews(w, sheet);
        WriteColumns(w, sheet);
        WriteSheetData(w, sheet, sst, styles);
        WriteMergeCells(w, sheet);

        w.WriteEndElement(); // worksheet
    }

    private static void WriteSheetViews(XmlWriter w, Sheet sheet)
    {
        w.WriteStartElement("sheetViews", Ns.SS);
        w.WriteStartElement("sheetView", Ns.SS);
        w.WriteAttributeString("workbookViewId", "0");

        if (sheet.FrozenPane is { } pane && (pane.Rows > 0 || pane.Columns > 0))
        {
            w.WriteStartElement("pane", Ns.SS);
            if (pane.Columns > 0)
                w.WriteAttributeString("xSplit", pane.Columns.ToString());
            if (pane.Rows > 0)
                w.WriteAttributeString("ySplit", pane.Rows.ToString());
            w.WriteAttributeString("topLeftCell", CellRef.FromIndex(pane.Rows, pane.Columns));
            w.WriteAttributeString("state", "frozen");
            w.WriteEndElement(); // pane
        }

        w.WriteEndElement(); // sheetView
        w.WriteEndElement(); // sheetViews
    }

    private static void WriteColumns(XmlWriter w, Sheet sheet)
    {
        if (sheet.Columns is not { Count: > 0 }) return;

        w.WriteStartElement("cols", Ns.SS);
        for (var i = 0; i < sheet.Columns.Count; i++)
        {
            var col = sheet.Columns[i];
            var colNum = (i + 1).ToString();
            w.WriteStartElement("col", Ns.SS);
            w.WriteAttributeString("min", colNum);
            w.WriteAttributeString("max", colNum);
            if (col.Width is not null)
            {
                w.WriteAttributeString("width", col.Width.Value.ToString("0.##", CultureInfo.InvariantCulture));
                w.WriteAttributeString("customWidth", "1");
            }
            if (col.Hidden)
                w.WriteAttributeString("hidden", "1");
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    private static void WriteSheetData(XmlWriter w, Sheet sheet, SharedStringTable sst, StyleCollector styles)
    {
        w.WriteStartElement("sheetData", Ns.SS);

        for (var rowIdx = 0; rowIdx < sheet.Rows.Count; rowIdx++)
        {
            var row = sheet.Rows[rowIdx];
            var rowNum = rowIdx + 1;

            w.WriteStartElement("row", Ns.SS);
            w.WriteAttributeString("r", rowNum.ToString());
            if (row.Height is not null)
            {
                w.WriteAttributeString("ht", row.Height.Value.ToString("0.##", CultureInfo.InvariantCulture));
                w.WriteAttributeString("customHeight", "1");
            }
            if (row.Hidden)
                w.WriteAttributeString("hidden", "1");

            for (var colIdx = 0; colIdx < row.Cells.Count; colIdx++)
            {
                var cell = row.Cells[colIdx];
                var cellRef = CellRef.FromIndex(rowIdx, colIdx);
                WriteCell(w, cellRef, cell, sst, styles);
            }

            w.WriteEndElement(); // row
        }

        w.WriteEndElement(); // sheetData
    }

    private static void WriteCell(XmlWriter w, string cellRef, Cell cell, SharedStringTable sst, StyleCollector styles)
    {
        var styleIdx = styles.GetStyleIndex(cell);

        w.WriteStartElement("c", Ns.SS);
        w.WriteAttributeString("r", cellRef);

        if (styleIdx != 0)
            w.WriteAttributeString("s", styleIdx.ToString());

        if (cell.Formula is not null)
        {
            // Formula cell. OOXML <f> content is the formula body WITHOUT the leading '='
            // (Excel rejects/duplicates the sign otherwise). WriteString XML-escapes the body,
            // so '<', '>', '&', and quotes inside the expression cannot break out of the element.
            w.WriteStartElement("f", Ns.SS);
            w.WriteString(NormalizeFormula(cell.Formula));
            w.WriteEndElement();

            // Write cached value if present. A string result must be tagged t="str" and written
            // literally; writing it through FormatNumericValue would emit a bare numeric <v> that
            // Excel treats as an invalid number.
            if (cell.Value is string cachedStr)
            {
                w.WriteAttributeString("t", "str");
                w.WriteStartElement("v", Ns.SS);
                w.WriteString(cachedStr);
                w.WriteEndElement();
            }
            else if (cell.Value is not null)
            {
                w.WriteStartElement("v", Ns.SS);
                w.WriteString(FormatNumericValue(cell.Value));
                w.WriteEndElement();
            }
        }
        else if (cell.Value is not null)
        {
            WriteCellValue(w, cell.Value, sst);
        }

        w.WriteEndElement(); // c
    }

    private static void WriteCellValue(XmlWriter w, object value, SharedStringTable sst)
    {
        switch (value)
        {
            case string s:
                var idx = sst.GetOrAdd(s);
                w.WriteAttributeString("t", "s");
                w.WriteStartElement("v", Ns.SS);
                w.WriteString(idx.ToString());
                w.WriteEndElement();
                break;

            case bool b:
                w.WriteAttributeString("t", "b");
                w.WriteStartElement("v", Ns.SS);
                w.WriteString(b ? "1" : "0");
                w.WriteEndElement();
                break;

            case DateTime dt:
                // Excel date serial number (days since 1900-01-01, with 1900 leap year bug)
                w.WriteStartElement("v", Ns.SS);
                w.WriteString(ToOADate(dt).ToString(CultureInfo.InvariantCulture));
                w.WriteEndElement();
                break;

            case DateTimeOffset dto:
                // Anchor on UTC so the Excel serial number is offset-independent and stable on
                // roundtrip. Using dto.DateTime (the local wall-clock part) silently dropped the
                // offset, shifting the serial date by the offset's hours.
                w.WriteStartElement("v", Ns.SS);
                w.WriteString(ToOADate(dto.UtcDateTime).ToString(CultureInfo.InvariantCulture));
                w.WriteEndElement();
                break;

            default:
                if (IsNumeric(value))
                {
                    w.WriteStartElement("v", Ns.SS);
                    w.WriteString(FormatNumericValue(value));
                    w.WriteEndElement();
                }
                else
                {
                    // Unsupported CLR type (e.g. Guid, char, custom object): emit its text form as an
                    // inline shared string, NOT a bare numeric <v> — otherwise Excel parses it as a
                    // number and shows an error. Honest text fallback.
                    var strIdx = sst.GetOrAdd(value.ToString() ?? "");
                    w.WriteAttributeString("t", "s");
                    w.WriteStartElement("v", Ns.SS);
                    w.WriteString(strIdx.ToString());
                    w.WriteEndElement();
                }
                break;
        }
    }

    private static bool IsNumeric(object value) =>
        value is double or decimal or float or int or long or short or byte or sbyte or uint or ulong or ushort;

    private static string FormatNumericValue(object value) => value switch
    {
        double d => d.ToString(CultureInfo.InvariantCulture),
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        float f => f.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        IFormattable fmt => fmt.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    /// <summary>Convert DateTime to OLE Automation date (Excel serial number).</summary>
    private static double ToOADate(DateTime dt) => dt.ToOADate();

    /// <summary>
    /// Normalize a formula for the OOXML &lt;f&gt; element. The stored body must NOT carry the
    /// leading '=' (and tolerates a stray leading apostrophe/tab a caller may have prepended as a
    /// CSV-style guard). XML-escaping of the body is handled by <see cref="XmlWriter.WriteString"/>,
    /// so this only strips characters that are invalid as the first token of a formula body.
    /// </summary>
    private static string NormalizeFormula(string formula)
    {
        var span = formula.AsSpan().TrimStart();
        // Drop a single leading '=' (Excel formula sign) or a caller-applied injection guard.
        if (span.Length > 0 && span[0] is '=' or '\'' or '\t')
            span = span[1..];
        return span.ToString();
    }

    private static void WriteMergeCells(XmlWriter w, Sheet sheet)
    {
        if (sheet.MergedCells is not { Count: > 0 }) return;

        w.WriteStartElement("mergeCells", Ns.SS);
        w.WriteAttributeString("count", sheet.MergedCells.Count.ToString());
        foreach (var merge in sheet.MergedCells)
        {
            w.WriteStartElement("mergeCell", Ns.SS);
            w.WriteAttributeString("ref", $"{merge.From}:{merge.To}");
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }
}
