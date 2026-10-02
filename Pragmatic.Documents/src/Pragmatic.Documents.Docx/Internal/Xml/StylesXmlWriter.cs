using System.Xml;

namespace Pragmatic.Documents.Docx.Internal.Xml;

/// <summary>Writes word/styles.xml with document defaults and heading styles, parametrized by DocxTheme.</summary>
internal static class StylesXmlWriter
{
    internal static void WriteTo(Stream stream, DocxTheme theme)
    {
        using var w = XmlWriter.Create(stream, XmlSettings.Default);
        w.WriteStartDocument(true);
        w.WriteStartElement("w", "styles", Ns.W);

        WriteDocumentDefaults(w, theme);
        WriteNormalStyle(w, theme);

        for (var level = 1; level <= 6; level++)
            WriteHeadingStyle(w, level, theme);

        WriteHyperlinkStyle(w, theme);
        WriteFootnoteStyles(w, theme);
        WriteTocStyles(w);

        w.WriteEndElement(); // styles
    }

    private static void WriteDocumentDefaults(XmlWriter w, DocxTheme theme)
    {
        w.WriteStartElement("w", "docDefaults", Ns.W);

        // Run defaults
        w.WriteStartElement("w", "rPrDefault", Ns.W);
        w.WriteStartElement("w", "rPr", Ns.W);
        WriteFont(w, theme.BodyFont);
        WriteFontSize(w, theme.BodyFontSize);
        w.WriteEndElement();
        w.WriteEndElement();

        // Paragraph defaults
        w.WriteStartElement("w", "pPrDefault", Ns.W);
        w.WriteStartElement("w", "pPr", Ns.W);
        w.WriteStartElement("w", "spacing", Ns.W);
        w.WriteAttributeString("w", "after", Ns.W, theme.DefaultSpacingAfter.ToString());
        w.WriteAttributeString("w", "line", Ns.W, theme.DefaultLineSpacing.ToString());
        w.WriteAttributeString("w", "lineRule", Ns.W, "auto");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteEndElement(); // docDefaults
    }

    private static void WriteNormalStyle(XmlWriter w, DocxTheme theme)
    {
        w.WriteStartElement("w", "style", Ns.W);
        w.WriteAttributeString("w", "type", Ns.W, "paragraph");
        w.WriteAttributeString("w", "styleId", Ns.W, "Normal");
        w.WriteAttributeString("w", "default", Ns.W, "1");
        WriteName(w, "Normal");
        w.WriteEndElement();
    }

    private static void WriteHeadingStyle(XmlWriter w, int level, DocxTheme theme)
    {
        var size = theme.HeadingFontSize(level);
        var (spacingBefore, spacingAfter) = theme.HeadingSpacing(level);
        var color = theme.HeadingColor(level);

        w.WriteStartElement("w", "style", Ns.W);
        w.WriteAttributeString("w", "type", Ns.W, "paragraph");
        w.WriteAttributeString("w", "styleId", Ns.W, $"Heading{level}");
        WriteName(w, $"heading {level}");

        w.WriteStartElement("w", "basedOn", Ns.W);
        w.WriteAttributeString("w", "val", Ns.W, "Normal");
        w.WriteEndElement();

        // Paragraph properties
        w.WriteStartElement("w", "pPr", Ns.W);
        w.WriteStartElement("w", "keepNext", Ns.W); w.WriteEndElement();
        w.WriteStartElement("w", "keepLines", Ns.W); w.WriteEndElement();
        w.WriteStartElement("w", "spacing", Ns.W);
        w.WriteAttributeString("w", "before", Ns.W, spacingBefore.ToString());
        w.WriteAttributeString("w", "after", Ns.W, spacingAfter.ToString());
        w.WriteEndElement();
        w.WriteStartElement("w", "outlineLvl", Ns.W);
        w.WriteAttributeString("w", "val", Ns.W, (level - 1).ToString());
        w.WriteEndElement();
        w.WriteEndElement(); // pPr

        // Run properties
        w.WriteStartElement("w", "rPr", Ns.W);
        WriteFont(w, theme.HeadingFont);
        WriteFontSize(w, size);
        if (theme.HeadingBold && level <= 2)
        {
            w.WriteStartElement("w", "b", Ns.W); w.WriteEndElement();
        }
        WriteColor(w, color);
        w.WriteEndElement(); // rPr

        w.WriteEndElement(); // style
    }

    private static void WriteHyperlinkStyle(XmlWriter w, DocxTheme theme)
    {
        w.WriteStartElement("w", "style", Ns.W);
        w.WriteAttributeString("w", "type", Ns.W, "character");
        w.WriteAttributeString("w", "styleId", Ns.W, "Hyperlink");
        WriteName(w, "Hyperlink");
        w.WriteStartElement("w", "rPr", Ns.W);
        WriteColor(w, theme.HyperlinkColor);
        w.WriteStartElement("w", "u", Ns.W);
        w.WriteAttributeString("w", "val", Ns.W, "single");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void WriteFootnoteStyles(XmlWriter w, DocxTheme theme)
    {
        // FootnoteText paragraph style
        w.WriteStartElement("w", "style", Ns.W);
        w.WriteAttributeString("w", "type", Ns.W, "paragraph");
        w.WriteAttributeString("w", "styleId", Ns.W, "FootnoteText");
        WriteName(w, "footnote text");
        w.WriteStartElement("w", "rPr", Ns.W);
        WriteFontSize(w, Math.Max(16, theme.BodyFontSize - 2)); // 1pt smaller than body
        w.WriteEndElement();
        w.WriteEndElement();

        // FootnoteReference character style
        w.WriteStartElement("w", "style", Ns.W);
        w.WriteAttributeString("w", "type", Ns.W, "character");
        w.WriteAttributeString("w", "styleId", Ns.W, "FootnoteReference");
        WriteName(w, "footnote reference");
        w.WriteStartElement("w", "rPr", Ns.W);
        w.WriteStartElement("w", "vertAlign", Ns.W);
        w.WriteAttributeString("w", "val", Ns.W, "superscript");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void WriteTocStyles(XmlWriter w)
    {
        for (var level = 1; level <= 3; level++)
        {
            w.WriteStartElement("w", "style", Ns.W);
            w.WriteAttributeString("w", "type", Ns.W, "paragraph");
            w.WriteAttributeString("w", "styleId", Ns.W, $"TOC{level}");
            WriteName(w, $"toc {level}");
            w.WriteEndElement();
        }
    }

    private static void WriteName(XmlWriter w, string name)
    {
        w.WriteStartElement("w", "name", Ns.W);
        w.WriteAttributeString("w", "val", Ns.W, name);
        w.WriteEndElement();
    }

    private static void WriteFont(XmlWriter w, string name)
    {
        w.WriteStartElement("w", "rFonts", Ns.W);
        w.WriteAttributeString("w", "ascii", Ns.W, name);
        w.WriteAttributeString("w", "hAnsi", Ns.W, name);
        w.WriteEndElement();
    }

    private static void WriteFontSize(XmlWriter w, int halfPoints)
    {
        w.WriteStartElement("w", "sz", Ns.W);
        w.WriteAttributeString("w", "val", Ns.W, halfPoints.ToString());
        w.WriteEndElement();
    }

    private static void WriteColor(XmlWriter w, string hex)
    {
        w.WriteStartElement("w", "color", Ns.W);
        w.WriteAttributeString("w", "val", Ns.W, hex);
        w.WriteEndElement();
    }
}
