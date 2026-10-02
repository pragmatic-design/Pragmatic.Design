using System.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal.Nodes;

/// <summary>Renders TableNode as w:tbl with columns, header, rows, colspan/rowspan.</summary>
internal static class TableNodeRenderer
{
    internal static void Render(XmlWriter w, TableNode node, DocxRenderContext ctx)
    {
        var colCount = node.Columns.Count;
        if (colCount == 0 && node.Rows.Count > 0)
            colCount = node.Rows[0].Cells.Count;
        if (colCount == 0) return;

        w.WriteStartElement("w", "tbl", Ns.W);

        // Table properties
        w.WriteStartElement("w", "tblPr", Ns.W);
        w.WriteStartElement("w", "tblStyle", Ns.W);
        w.WriteAttributeString("w", "val", Ns.W, "TableGrid");
        w.WriteEndElement();
        w.WriteStartElement("w", "tblW", Ns.W);
        w.WriteAttributeString("w", "w", Ns.W, "5000");
        w.WriteAttributeString("w", "type", Ns.W, "pct");
        w.WriteEndElement();
        // Borders
        WriteBorders(w);
        w.WriteEndElement(); // tblPr

        // Grid columns
        w.WriteStartElement("w", "tblGrid", Ns.W);
        foreach (var col in node.Columns)
        {
            w.WriteStartElement("w", "gridCol", Ns.W);
            if (col.Width.HasValue)
                w.WriteAttributeString("w", "w", Ns.W, Units.MmToTwips(col.Width.Value).ToString());
            w.WriteEndElement();
        }
        // Fill remaining columns
        for (var i = node.Columns.Count; i < colCount; i++)
        {
            w.WriteStartElement("w", "gridCol", Ns.W);
            w.WriteEndElement();
        }
        w.WriteEndElement(); // tblGrid

        // Header row
        if (node.Header is not null)
            WriteRow(w, node.Header, ctx, isHeader: node.RepeatHeader);

        // Data rows with rowspan continuation tracking
        // activeRowSpans[col] = remaining rows to merge; activeColSpans[col] = original colSpan width (0 = not a merge start)
        var activeRowSpans = new int[colCount];
        var activeColSpans = new int[colCount];
        if (node.Header is not null)
        {
            for (var c = 0; c < node.Header.Cells.Count && c < colCount; c++)
            {
                if (node.Header.Cells[c].RowSpan > 1)
                {
                    var cs = Math.Max(1, node.Header.Cells[c].ColSpan);
                    for (var s = 0; s < cs && c + s < colCount; s++)
                    {
                        activeRowSpans[c + s] = node.Header.Cells[c].RowSpan - 1;
                        activeColSpans[c + s] = s == 0 ? cs : -1;
                    }
                }
            }
        }

        foreach (var row in node.Rows)
            WriteRowWithMerge(w, row, ctx, activeRowSpans, activeColSpans, colCount);

        w.WriteEndElement(); // tbl
    }

    private static void WriteRowWithMerge(XmlWriter w, TableRow row, DocxRenderContext ctx, int[] activeRowSpans, int[] activeColSpans, int colCount)
    {
        w.WriteStartElement("w", "tr", Ns.W);
        var cellIdx = 0;
        for (var col = 0; col < colCount; col++)
        {
            if (activeRowSpans[col] > 0)
            {
                // Skip columns that are inside an already-handled span (-1 marks inner columns).
                if (activeColSpans[col] == -1)
                {
                    activeRowSpans[col]--;
                    continue;
                }

                // Continuation cell for vertical merge — include gridSpan if original cell had colSpan.
                var mergeWidth = activeColSpans[col] > 1 ? activeColSpans[col] : 0;
                w.WriteStartElement("w", "tc", Ns.W);
                w.WriteStartElement("w", "tcPr", Ns.W);
                if (mergeWidth > 1)
                {
                    w.WriteStartElement("w", "gridSpan", Ns.W);
                    w.WriteAttributeString("w", "val", Ns.W, mergeWidth.ToString());
                    w.WriteEndElement();
                }
                w.WriteStartElement("w", "vMerge", Ns.W);
                w.WriteEndElement();
                w.WriteEndElement(); // tcPr
                w.WriteStartElement("w", "p", Ns.W);
                w.WriteEndElement();
                w.WriteEndElement(); // tc
                // Consume all columns covered by the original colSpan
                var skip = Math.Max(1, mergeWidth);
                for (var s = 0; s < skip && col + s < colCount; s++)
                    activeRowSpans[col + s]--;
                col += skip - 1;
            }
            else if (cellIdx < row.Cells.Count)
            {
                var cell = row.Cells[cellIdx++];
                WriteCell(w, cell, ctx);
                var span = Math.Max(1, cell.ColSpan);
                if (cell.RowSpan > 1)
                {
                    for (var s = 0; s < span && col + s < colCount; s++)
                    {
                        activeRowSpans[col + s] = cell.RowSpan - 1;
                        // Store the span width on the first column; mark the rest as -1 (inside a span).
                        activeColSpans[col + s] = s == 0 ? span : -1;
                    }
                }
                col += span - 1;
            }
            else
            {
                w.WriteStartElement("w", "tc", Ns.W);
                w.WriteStartElement("w", "p", Ns.W);
                w.WriteEndElement();
                w.WriteEndElement();
            }
        }
        w.WriteEndElement();
    }

    private static void WriteRow(XmlWriter w, TableRow row, DocxRenderContext ctx, bool isHeader)
    {
        w.WriteStartElement("w", "tr", Ns.W);

        if (isHeader)
        {
            w.WriteStartElement("w", "trPr", Ns.W);
            w.WriteStartElement("w", "tblHeader", Ns.W);
            w.WriteEndElement();
            w.WriteEndElement();
        }

        foreach (var cell in row.Cells)
            WriteCell(w, cell, ctx);

        w.WriteEndElement(); // tr
    }

    private static void WriteCell(XmlWriter w, TableCell cell, DocxRenderContext ctx)
    {
        w.WriteStartElement("w", "tc", Ns.W);

        // Cell properties
        if (cell.ColSpan > 1 || cell.RowSpan > 1 || cell.Style?.CellVerticalAlign is not null || cell.Style?.BackgroundColor is not null)
        {
            w.WriteStartElement("w", "tcPr", Ns.W);

            if (cell.ColSpan > 1)
            {
                w.WriteStartElement("w", "gridSpan", Ns.W);
                w.WriteAttributeString("w", "val", Ns.W, cell.ColSpan.ToString());
                w.WriteEndElement();
            }

            if (cell.RowSpan > 1)
            {
                w.WriteStartElement("w", "vMerge", Ns.W);
                w.WriteAttributeString("w", "val", Ns.W, "restart");
                w.WriteEndElement();
            }

            if (cell.Style?.CellVerticalAlign is not null)
            {
                w.WriteStartElement("w", "vAlign", Ns.W);
                w.WriteAttributeString("w", "val", Ns.W, cell.Style.CellVerticalAlign switch
                {
                    CellVerticalAlign.Middle => "center",
                    CellVerticalAlign.Bottom => "bottom",
                    _ => "top"
                });
                w.WriteEndElement();
            }

            if (cell.Style?.BackgroundColor is not null)
            {
                w.WriteStartElement("w", "shd", Ns.W);
                w.WriteAttributeString("w", "val", Ns.W, "clear");
                w.WriteAttributeString("w", "fill", Ns.W, cell.Style.BackgroundColor.TrimStart('#'));
                w.WriteEndElement();
            }

            w.WriteEndElement(); // tcPr
        }

        // Cell must contain at least one paragraph
        if (cell.Content.Count == 0)
        {
            w.WriteStartElement("w", "p", Ns.W);
            w.WriteEndElement();
        }
        else
        {
            NodeRenderer.RenderAll(w, cell.Content, ctx);
        }

        w.WriteEndElement(); // tc
    }

    private static void WriteBorders(XmlWriter w)
    {
        w.WriteStartElement("w", "tblBorders", Ns.W);
        foreach (var side in new[] { "top", "left", "bottom", "right", "insideH", "insideV" })
        {
            w.WriteStartElement("w", side, Ns.W);
            w.WriteAttributeString("w", "val", Ns.W, "single");
            w.WriteAttributeString("w", "sz", Ns.W, "4");
            w.WriteAttributeString("w", "space", Ns.W, "0");
            w.WriteAttributeString("w", "color", Ns.W, "auto");
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }
}
