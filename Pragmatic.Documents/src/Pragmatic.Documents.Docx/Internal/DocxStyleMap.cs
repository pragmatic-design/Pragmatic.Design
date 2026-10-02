using System.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal;

/// <summary>Maps NodeStyle properties to OOXML run properties (w:rPr) and paragraph properties (w:pPr).</summary>
internal static class DocxStyleMap
{
    /// <summary>
    ///     The run formatting a text run is written with: its own, and the enclosing block's where the run
    ///     sets nothing — property by property, as in CSS.
    /// </summary>
    /// <remarks>
    ///     A heading, a paragraph and a link are blocks; font, size, weight, italic, underline, strike,
    ///     vertical position, colour, highlight and letter spacing cascade from them. Alignment, spacing and
    ///     indents do not: they are the paragraph's, and <see cref="WriteRunProperties" /> reads none of them.
    /// </remarks>
    internal static NodeStyle? InheritRunFormatting(NodeStyle? block, NodeStyle? run)
    {
        if (block is null) return run;
        if (run is null) return block;

        return run with
        {
            FontFamily = run.FontFamily ?? block.FontFamily,
            FontSize = run.FontSize ?? block.FontSize,
            FontWeight = run.FontWeight ?? block.FontWeight,
            Italic = run.Italic ?? block.Italic,
            Underline = run.Underline ?? block.Underline,
            Strikethrough = run.Strikethrough ?? block.Strikethrough,
            VerticalPosition = run.VerticalPosition ?? block.VerticalPosition,
            Color = run.Color ?? block.Color,
            HighlightColor = run.HighlightColor ?? block.HighlightColor,
            LetterSpacing = run.LetterSpacing ?? block.LetterSpacing
        };
    }

    /// <summary>Write w:rPr (run properties) from NodeStyle, optionally with a character style ID.</summary>
    internal static void WriteRunProperties(XmlWriter w, NodeStyle? style, string? runStyleId = null)
    {
        if (style is null && runStyleId is null) return;

        w.WriteStartElement("w", "rPr", Ns.W);

        // First, as the schema orders w:rPr's children.
        if (runStyleId is not null)
        {
            w.WriteStartElement("w", "rStyle", Ns.W);
            w.WriteAttributeString("w", "val", Ns.W, runStyleId);
            w.WriteEndElement();
        }

        if (style is null)
        {
            w.WriteEndElement(); // rPr
            return;
        }

        if (style.FontFamily is not null)
        {
            w.WriteStartElement("w", "rFonts", Ns.W);
            w.WriteAttributeString("w", "ascii", Ns.W, style.FontFamily);
            w.WriteAttributeString("w", "hAnsi", Ns.W, style.FontFamily);
            w.WriteEndElement();
        }

        if (style.FontWeight == FontWeight.Bold)
        {
            w.WriteStartElement("w", "b", Ns.W); w.WriteEndElement();
        }

        if (style.Italic == true)
        {
            w.WriteStartElement("w", "i", Ns.W); w.WriteEndElement();
        }

        if (style.Underline == true)
        {
            w.WriteStartElement("w", "u", Ns.W);
            w.WriteAttributeString("w", "val", Ns.W, "single");
            w.WriteEndElement();
        }

        if (style.Strikethrough == true)
        {
            w.WriteStartElement("w", "strike", Ns.W); w.WriteEndElement();
        }

        if (style.VerticalPosition is VerticalPosition.Superscript or VerticalPosition.Subscript)
        {
            w.WriteStartElement("w", "vertAlign", Ns.W);
            w.WriteAttributeString("w", "val", Ns.W,
                style.VerticalPosition == VerticalPosition.Superscript ? "superscript" : "subscript");
            w.WriteEndElement();
        }

        if (style.FontSize.HasValue)
        {
            var halfPt = Units.PtToHalfPoints(style.FontSize.Value);
            w.WriteStartElement("w", "sz", Ns.W);
            w.WriteAttributeString("w", "val", Ns.W, halfPt.ToString());
            w.WriteEndElement();
        }

        if (style.Color is not null)
        {
            w.WriteStartElement("w", "color", Ns.W);
            w.WriteAttributeString("w", "val", Ns.W, style.Color.TrimStart('#'));
            w.WriteEndElement();
        }

        if (style.HighlightColor is not null)
        {
            w.WriteStartElement("w", "highlight", Ns.W);
            w.WriteAttributeString("w", "val", Ns.W, style.HighlightColor);
            w.WriteEndElement();
        }

        if (style.LetterSpacing.HasValue)
        {
            // w:spacing in w:rPr is in twentieths of a point (twips); LetterSpacing is in mm, like every
            // other length of NodeStyle.
            w.WriteStartElement("w", "spacing", Ns.W);
            w.WriteAttributeString("w", "val", Ns.W, Units.MmToTwips(style.LetterSpacing.Value).ToString());
            w.WriteEndElement();
        }

        w.WriteEndElement(); // rPr
    }

    /// <summary>Write w:pPr (paragraph properties) from NodeStyle, optionally with a style ID.</summary>
    internal static void WriteParagraphProperties(XmlWriter w, NodeStyle? style, string? styleId = null)
    {
        if (style is null && styleId is null) return;

        w.WriteStartElement("w", "pPr", Ns.W);

        if (styleId is not null)
        {
            w.WriteStartElement("w", "pStyle", Ns.W);
            w.WriteAttributeString("w", "val", Ns.W, styleId);
            w.WriteEndElement();
        }

        if (style?.TextAlign is not null)
        {
            w.WriteStartElement("w", "jc", Ns.W);
            w.WriteAttributeString("w", "val", Ns.W, style.TextAlign switch
            {
                TextAlign.Center => "center",
                TextAlign.Right => "right",
                TextAlign.Justify => "both",
                _ => "left"
            });
            w.WriteEndElement();
        }

        // Spacing (margin top/bottom → spacing before/after, line height)
        if (style?.MarginTop.HasValue == true || style?.MarginBottom.HasValue == true || style?.LineHeight.HasValue == true)
        {
            w.WriteStartElement("w", "spacing", Ns.W);
            if (style.MarginTop.HasValue)
                w.WriteAttributeString("w", "before", Ns.W, Units.MmToTwips(style.MarginTop.Value).ToString());
            if (style.MarginBottom.HasValue)
                w.WriteAttributeString("w", "after", Ns.W, Units.MmToTwips(style.MarginBottom.Value).ToString());
            if (style.LineHeight.HasValue)
            {
                // Line height as multiplier: 1.0 = 240 twips, 1.5 = 360, 2.0 = 480
                var lineVal = (int)Math.Round(style.LineHeight.Value * 240);
                w.WriteAttributeString("w", "line", Ns.W, lineVal.ToString());
                w.WriteAttributeString("w", "lineRule", Ns.W, "auto");
            }
            w.WriteEndElement();
        }

        // Indentation (margin left/right, first line)
        if (style?.MarginLeft.HasValue == true || style?.MarginRight.HasValue == true || style?.FirstLineIndent.HasValue == true)
        {
            w.WriteStartElement("w", "ind", Ns.W);
            if (style.MarginLeft.HasValue)
                w.WriteAttributeString("w", "left", Ns.W, Units.MmToTwips(style.MarginLeft.Value).ToString());
            if (style.MarginRight.HasValue)
                w.WriteAttributeString("w", "right", Ns.W, Units.MmToTwips(style.MarginRight.Value).ToString());
            if (style.FirstLineIndent.HasValue)
                w.WriteAttributeString("w", "firstLine", Ns.W, Units.MmToTwips(style.FirstLineIndent.Value).ToString());
            w.WriteEndElement();
        }

        w.WriteEndElement(); // pPr
    }

    /// <summary>Check if a style has any run-level properties set.</summary>
    internal static bool HasRunProperties(NodeStyle? style) =>
        style is not null && (
            style.FontFamily is not null ||
            style.FontWeight is not null ||
            style.Italic == true ||
            style.Underline == true ||
            style.Strikethrough == true ||
            style.VerticalPosition is not null ||
            style.FontSize.HasValue ||
            style.Color is not null ||
            style.HighlightColor is not null ||
            style.LetterSpacing.HasValue);
}
