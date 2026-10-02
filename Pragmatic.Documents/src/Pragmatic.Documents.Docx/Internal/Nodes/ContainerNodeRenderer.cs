using System.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal.Nodes;

internal static class ContainerNodeRenderer
{
    internal static void Render(XmlWriter w, ContainerNode node, DocxRenderContext ctx)
        => NodeRenderer.RenderAll(w, node.Children, ctx);
}
