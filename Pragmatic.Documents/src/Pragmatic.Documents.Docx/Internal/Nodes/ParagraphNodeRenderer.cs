using System.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal.Nodes;

/// <summary>Renders ParagraphNode as w:p with inline children.</summary>
internal static class ParagraphNodeRenderer
{
    internal static void Render(XmlWriter w, ParagraphNode node, DocxRenderContext ctx)
    {
        w.WriteStartElement("w", "p", Ns.W);
        DocxStyleMap.WriteParagraphProperties(w, node.Style);

        foreach (var child in node.Children)
            NodeRenderer.RenderInline(w, child, ctx, node.Style);

        w.WriteEndElement(); // p
    }
}
