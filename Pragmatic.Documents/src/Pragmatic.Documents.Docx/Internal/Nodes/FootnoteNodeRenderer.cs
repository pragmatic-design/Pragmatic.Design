using System.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal.Nodes;

/// <summary>Renders FootnoteNode as an inline footnote reference.</summary>
internal static class FootnoteNodeRenderer
{
    /// <summary>Render as a full paragraph with the footnote reference.</summary>
    internal static void Render(XmlWriter w, FootnoteNode node, DocxRenderContext ctx)
    {
        w.WriteStartElement("w", "p", Ns.W);
        RenderInline(w, node, ctx);
        w.WriteEndElement();
    }

    /// <summary>Render just the inline footnote reference run.</summary>
    internal static void RenderInline(XmlWriter w, FootnoteNode node, DocxRenderContext ctx)
    {
        var footnoteId = ctx.AddFootnote(node.Content);
        // footnotes.xml offsets by 2 (separator footnotes at 0 and 1), so the actual OOXML id = footnoteId + 1.
        var oomlId = footnoteId + 1;

        w.WriteStartElement("w", "r", Ns.W);
        w.WriteStartElement("w", "rPr", Ns.W);
        w.WriteStartElement("w", "rStyle", Ns.W);
        w.WriteAttributeString("w", "val", Ns.W, "FootnoteReference");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteStartElement("w", "footnoteReference", Ns.W);
        w.WriteAttributeString("w", "id", Ns.W, oomlId.ToString());
        w.WriteEndElement();
        w.WriteEndElement();
    }
}
