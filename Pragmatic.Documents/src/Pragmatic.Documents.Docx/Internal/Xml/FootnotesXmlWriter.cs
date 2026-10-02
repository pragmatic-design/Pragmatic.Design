using System.Xml;

namespace Pragmatic.Documents.Docx.Internal.Xml;

/// <summary>Writes word/footnotes.xml with collected footnote bodies.</summary>
internal static class FootnotesXmlWriter
{
    internal static void WriteTo(Stream stream, DocxRenderContext ctx)
    {
        using var w = XmlWriter.Create(stream, XmlSettings.Default);
        w.WriteStartDocument(true);
        w.WriteStartElement("w", "footnotes", Ns.W);

        // Required separator footnotes (id 0 and 1)
        WriteSeparatorFootnote(w, 0, "separator");
        WriteSeparatorFootnote(w, 1, "continuationSeparator");

        // User footnotes (1-based, but OOXML IDs start at 2 since 0 and 1 are reserved)
        for (var i = 0; i < ctx.Footnotes.Count; i++)
        {
            var id = i + 2; // offset by 2 for separator footnotes
            w.WriteStartElement("w", "footnote", Ns.W);
            w.WriteAttributeString("w", "id", Ns.W, id.ToString());

            w.WriteStartElement("w", "p", Ns.W);

            // Paragraph style
            w.WriteStartElement("w", "pPr", Ns.W);
            w.WriteStartElement("w", "pStyle", Ns.W);
            w.WriteAttributeString("w", "val", Ns.W, "FootnoteText");
            w.WriteEndElement();
            w.WriteEndElement();

            // Footnote reference mark
            w.WriteStartElement("w", "r", Ns.W);
            w.WriteStartElement("w", "rPr", Ns.W);
            w.WriteStartElement("w", "rStyle", Ns.W);
            w.WriteAttributeString("w", "val", Ns.W, "FootnoteReference");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("w", "footnoteRef", Ns.W);
            w.WriteEndElement();
            w.WriteEndElement();

            // Space + text
            w.WriteStartElement("w", "r", Ns.W);
            w.WriteStartElement("w", "t", Ns.W);
            w.WriteAttributeString("xml", "space", null, "preserve");
            w.WriteString(" " + ctx.Footnotes[i]);
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteEndElement(); // p
            w.WriteEndElement(); // footnote
        }

        w.WriteEndElement(); // footnotes
    }

    private static void WriteSeparatorFootnote(XmlWriter w, int id, string type)
    {
        w.WriteStartElement("w", "footnote", Ns.W);
        w.WriteAttributeString("w", "type", Ns.W, type);
        w.WriteAttributeString("w", "id", Ns.W, id.ToString());

        w.WriteStartElement("w", "p", Ns.W);
        w.WriteStartElement("w", "r", Ns.W);
        if (type == "separator")
        {
            w.WriteStartElement("w", "separator", Ns.W);
            w.WriteEndElement();
        }
        else
        {
            w.WriteStartElement("w", "continuationSeparator", Ns.W);
            w.WriteEndElement();
        }
        w.WriteEndElement(); // r
        w.WriteEndElement(); // p
        w.WriteEndElement(); // footnote
    }
}
