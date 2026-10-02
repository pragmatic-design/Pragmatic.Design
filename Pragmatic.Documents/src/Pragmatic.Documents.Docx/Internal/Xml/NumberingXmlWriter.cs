using System.Xml;

namespace Pragmatic.Documents.Docx.Internal.Xml;

/// <summary>Writes word/numbering.xml (list numbering definitions).</summary>
internal static class NumberingXmlWriter
{
    internal static void WriteTo(Stream stream, DocxRenderContext ctx)
    {
        using var w = XmlWriter.Create(stream, XmlSettings.Default);
        w.WriteStartDocument(true);
        w.WriteStartElement("w", "numbering", Ns.W);

        foreach (var def in ctx.NumberingDefs)
        {
            // Abstract numbering definition
            w.WriteStartElement("w", "abstractNum", Ns.W);
            w.WriteAttributeString("w", "abstractNumId", Ns.W, def.AbstractNumId.ToString());

            // Level 0 (support up to 9 levels for nested lists)
            for (var level = 0; level < 9; level++)
            {
                w.WriteStartElement("w", "lvl", Ns.W);
                w.WriteAttributeString("w", "ilvl", Ns.W, level.ToString());

                w.WriteStartElement("w", "start", Ns.W);
                w.WriteAttributeString("w", "val", Ns.W, "1");
                w.WriteEndElement();

                w.WriteStartElement("w", "numFmt", Ns.W);
                w.WriteAttributeString("w", "val", Ns.W, def.Ordered ? "decimal" : "bullet");
                w.WriteEndElement();

                // Bullet characters by level: •, ◦, ▪, •, ◦, ▪, ...
                var bulletChar = def.Ordered ? $"%{level + 1}." : (level % 3) switch
                {
                    0 => "\uF0B7", // bullet (Wingdings)
                    1 => "o",       // hollow circle
                    _ => "\uF0A7"  // square (Wingdings)
                };
                var bulletFont = !def.Ordered ? (level % 3) switch
                {
                    0 => "Symbol",
                    1 => "Courier New",
                    _ => "Wingdings"
                } : (string?)null;

                w.WriteStartElement("w", "lvlText", Ns.W);
                w.WriteAttributeString("w", "val", Ns.W, bulletChar);
                w.WriteEndElement();

                w.WriteStartElement("w", "lvlJc", Ns.W);
                w.WriteAttributeString("w", "val", Ns.W, "left");
                w.WriteEndElement();

                // Indentation: 360 twips per level
                w.WriteStartElement("w", "pPr", Ns.W);
                w.WriteStartElement("w", "ind", Ns.W);
                w.WriteAttributeString("w", "left", Ns.W, (360 * (level + 1)).ToString());
                w.WriteAttributeString("w", "hanging", Ns.W, "360");
                w.WriteEndElement();
                w.WriteEndElement();

                if (bulletFont is not null)
                {
                    w.WriteStartElement("w", "rPr", Ns.W);
                    w.WriteStartElement("w", "rFonts", Ns.W);
                    w.WriteAttributeString("w", "ascii", Ns.W, bulletFont);
                    w.WriteAttributeString("w", "hAnsi", Ns.W, bulletFont);
                    w.WriteAttributeString("w", "hint", Ns.W, "default");
                    w.WriteEndElement();
                    w.WriteEndElement();
                }

                w.WriteEndElement(); // lvl
            }

            w.WriteEndElement(); // abstractNum

            // Concrete numbering instance
            w.WriteStartElement("w", "num", Ns.W);
            w.WriteAttributeString("w", "numId", Ns.W, def.NumId.ToString());
            w.WriteStartElement("w", "abstractNumId", Ns.W);
            w.WriteAttributeString("w", "val", Ns.W, def.AbstractNumId.ToString());
            w.WriteEndElement();
            w.WriteEndElement(); // num
        }

        w.WriteEndElement(); // numbering
    }
}
