using System.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal.Nodes;

/// <summary>Renders FieldNode as a simple field code.</summary>
internal static class FieldNodeRenderer
{
    /// <summary>Render as a full paragraph.</summary>
    internal static void Render(XmlWriter w, FieldNode node, DocxRenderContext ctx)
    {
        w.WriteStartElement("w", "p", Ns.W);
        RenderInline(w, node, ctx);
        w.WriteEndElement();
    }

    /// <summary>Render as an inline field.</summary>
    internal static void RenderInline(XmlWriter w, FieldNode node, DocxRenderContext ctx)
    {
        var fieldCode = node.FieldType switch
        {
            FieldType.Page => "PAGE",
            FieldType.NumPages => "NUMPAGES",
            // Strip embedded double-quotes from the format picture: an unescaped '"' would close the
            // \@ switch and let attacker-controlled Format inject arbitrary field-code instructions.
            FieldType.Date when node.Format is not null => $"DATE \\@ \"{node.Format.Replace("\"", "")}\"",
            FieldType.Date => "DATE",
            FieldType.Time => "TIME",
            FieldType.FileName => "FILENAME",
            _ => "PAGE"
        };

        w.WriteStartElement("w", "r", Ns.W);
        w.WriteStartElement("w", "fldChar", Ns.W);
        w.WriteAttributeString("w", "fldCharType", Ns.W, "begin");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("w", "r", Ns.W);
        w.WriteStartElement("w", "instrText", Ns.W);
        w.WriteAttributeString("xml", "space", null, "preserve");
        w.WriteString($" {fieldCode} ");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("w", "r", Ns.W);
        w.WriteStartElement("w", "fldChar", Ns.W);
        w.WriteAttributeString("w", "fldCharType", Ns.W, "separate");
        w.WriteEndElement();
        w.WriteEndElement();

        // Placeholder
        w.WriteStartElement("w", "r", Ns.W);
        w.WriteStartElement("w", "t", Ns.W);
        // Placeholder text only — Word recalculates DATE/TIME via the field code on open.
        // Sourced from the render context so the value is consistent and injectable (tests,
        // reproducible builds) rather than coupled to the wall clock at render time.
        var now = ctx.RenderTimestamp;
        w.WriteString(node.FieldType switch
        {
            FieldType.Page => "1",
            FieldType.NumPages => "1",
            FieldType.Date => now.LocalDateTime.ToShortDateString(),
            FieldType.Time => now.LocalDateTime.ToShortTimeString(),
            _ => ""
        });
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("w", "r", Ns.W);
        w.WriteStartElement("w", "fldChar", Ns.W);
        w.WriteAttributeString("w", "fldCharType", Ns.W, "end");
        w.WriteEndElement();
        w.WriteEndElement();
    }
}
