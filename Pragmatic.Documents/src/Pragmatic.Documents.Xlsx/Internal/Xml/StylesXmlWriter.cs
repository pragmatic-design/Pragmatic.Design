using System.Xml;
using Pragmatic.Documents.Ooxml;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Xlsx.Internal.Xml;

/// <summary>Writes xl/styles.xml with indexed fonts, fills, borders, numFmts, and cellXfs.</summary>
internal static class StylesXmlWriter
{
    internal static void WriteTo(Stream stream, StyleCollector styles)
    {
        using var w = XmlWriter.Create(stream, OoxmlXmlSettings.Default);
        w.WriteStartDocument(true);
        w.WriteStartElement("styleSheet", Ns.SS);

        WriteNumFmts(w, styles);
        WriteFonts(w, styles);
        WriteFills(w, styles);
        WriteBorders(w, styles);
        WriteCellStyleXfs(w);
        WriteCellXfs(w, styles);

        w.WriteEndElement(); // styleSheet
    }

    private static void WriteNumFmts(XmlWriter w, StyleCollector styles)
    {
        if (styles.NumFmts.Count == 0) return;
        w.WriteStartElement("numFmts", Ns.SS);
        w.WriteAttributeString("count", styles.NumFmts.Count.ToString());
        foreach (var nf in styles.NumFmts)
        {
            w.WriteStartElement("numFmt", Ns.SS);
            w.WriteAttributeString("numFmtId", nf.Id.ToString());
            w.WriteAttributeString("formatCode", nf.FormatCode);
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    private static void WriteFonts(XmlWriter w, StyleCollector styles)
    {
        w.WriteStartElement("fonts", Ns.SS);
        w.WriteAttributeString("count", styles.Fonts.Count.ToString());
        foreach (var f in styles.Fonts)
        {
            w.WriteStartElement("font", Ns.SS);
            if (f.Bold) { w.WriteStartElement("b", Ns.SS); w.WriteEndElement(); }
            if (f.Italic) { w.WriteStartElement("i", Ns.SS); w.WriteEndElement(); }
            w.WriteStartElement("sz", Ns.SS); w.WriteAttributeString("val", f.Size.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)); w.WriteEndElement();
            if (f.Color is not null)
            {
                w.WriteStartElement("color", Ns.SS); w.WriteAttributeString("rgb", $"FF{f.Color}"); w.WriteEndElement();
            }
            w.WriteStartElement("name", Ns.SS); w.WriteAttributeString("val", f.Family); w.WriteEndElement();
            w.WriteEndElement(); // font
        }
        w.WriteEndElement();
    }

    private static void WriteFills(XmlWriter w, StyleCollector styles)
    {
        w.WriteStartElement("fills", Ns.SS);
        w.WriteAttributeString("count", styles.Fills.Count.ToString());

        // Fill 0: none
        w.WriteStartElement("fill", Ns.SS);
        w.WriteStartElement("patternFill", Ns.SS); w.WriteAttributeString("patternType", "none"); w.WriteEndElement();
        w.WriteEndElement();

        // Fill 1: gray125 (required by Excel)
        w.WriteStartElement("fill", Ns.SS);
        w.WriteStartElement("patternFill", Ns.SS); w.WriteAttributeString("patternType", "gray125"); w.WriteEndElement();
        w.WriteEndElement();

        // Custom fills
        for (var i = 2; i < styles.Fills.Count; i++)
        {
            var fill = styles.Fills[i];
            w.WriteStartElement("fill", Ns.SS);
            w.WriteStartElement("patternFill", Ns.SS);
            w.WriteAttributeString("patternType", "solid");
            w.WriteStartElement("fgColor", Ns.SS); w.WriteAttributeString("rgb", $"FF{fill.BackgroundColor}"); w.WriteEndElement();
            w.WriteStartElement("bgColor", Ns.SS); w.WriteAttributeString("indexed", "64"); w.WriteEndElement();
            w.WriteEndElement(); // patternFill
            w.WriteEndElement(); // fill
        }
        w.WriteEndElement();
    }

    private static void WriteBorders(XmlWriter w, StyleCollector styles)
    {
        w.WriteStartElement("borders", Ns.SS);
        w.WriteAttributeString("count", styles.Borders.Count.ToString());
        foreach (var b in styles.Borders)
        {
            w.WriteStartElement("border", Ns.SS);
            WriteBorderSide(w, "left", b.Left);
            WriteBorderSide(w, "right", b.Right);
            WriteBorderSide(w, "top", b.Top);
            WriteBorderSide(w, "bottom", b.Bottom);
            w.WriteStartElement("diagonal", Ns.SS); w.WriteEndElement();
            w.WriteEndElement(); // border
        }
        w.WriteEndElement();
    }

    private static void WriteBorderSide(XmlWriter w, string element, BorderSide? side)
    {
        w.WriteStartElement(element, Ns.SS);
        if (side is not null && side.Style != BorderLineStyle.None)
        {
            w.WriteAttributeString("style", MapBorderStyle(side.Style));
            w.WriteStartElement("color", Ns.SS); w.WriteAttributeString("rgb", $"FF{side.Color}"); w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    private static string MapBorderStyle(BorderLineStyle style) => style switch
    {
        BorderLineStyle.Thin => "thin",
        BorderLineStyle.Medium => "medium",
        BorderLineStyle.Thick => "thick",
        BorderLineStyle.Dashed => "dashed",
        BorderLineStyle.Dotted => "dotted",
        BorderLineStyle.Double => "double",
        _ => "thin"
    };

    private static void WriteCellStyleXfs(XmlWriter w)
    {
        // Required: at least 1 cellStyleXf (the default)
        w.WriteStartElement("cellStyleXfs", Ns.SS);
        w.WriteAttributeString("count", "1");
        w.WriteStartElement("xf", Ns.SS);
        w.WriteAttributeString("numFmtId", "0");
        w.WriteAttributeString("fontId", "0");
        w.WriteAttributeString("fillId", "0");
        w.WriteAttributeString("borderId", "0");
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void WriteCellXfs(XmlWriter w, StyleCollector styles)
    {
        w.WriteStartElement("cellXfs", Ns.SS);
        w.WriteAttributeString("count", styles.CellXfs.Count.ToString());
        foreach (var xf in styles.CellXfs)
        {
            w.WriteStartElement("xf", Ns.SS);
            w.WriteAttributeString("numFmtId", xf.NumFmtId.ToString());
            w.WriteAttributeString("fontId", xf.FontId.ToString());
            w.WriteAttributeString("fillId", xf.FillId.ToString());
            w.WriteAttributeString("borderId", xf.BorderId.ToString());
            w.WriteAttributeString("xfId", "0");

            if (xf.FontId != 0) w.WriteAttributeString("applyFont", "1");
            if (xf.FillId != 0) w.WriteAttributeString("applyFill", "1");
            if (xf.BorderId != 0) w.WriteAttributeString("applyBorder", "1");
            if (xf.NumFmtId != 0) w.WriteAttributeString("applyNumberFormat", "1");

            var hasAlign = xf.HorizontalAlign is not null || xf.VerticalAlign is not null || xf.WrapText;
            if (hasAlign)
            {
                w.WriteAttributeString("applyAlignment", "1");
                w.WriteStartElement("alignment", Ns.SS);
                if (xf.HorizontalAlign is not null)
                    w.WriteAttributeString("horizontal", MapHorizontalAlign(xf.HorizontalAlign.Value));
                if (xf.VerticalAlign is not null)
                    w.WriteAttributeString("vertical", MapVerticalAlign(xf.VerticalAlign.Value));
                if (xf.WrapText)
                    w.WriteAttributeString("wrapText", "1");
                w.WriteEndElement();
            }

            w.WriteEndElement(); // xf
        }
        w.WriteEndElement();
    }

    private static string MapHorizontalAlign(HorizontalAlign align) => align switch
    {
        HorizontalAlign.Left => "left",
        HorizontalAlign.Center => "center",
        HorizontalAlign.Right => "right",
        HorizontalAlign.Fill => "fill",
        HorizontalAlign.Justify => "justify",
        _ => "general"
    };

    private static string MapVerticalAlign(VerticalAlign align) => align switch
    {
        VerticalAlign.Top => "top",
        VerticalAlign.Center => "center",
        VerticalAlign.Bottom => "bottom",
        _ => "top"
    };
}
