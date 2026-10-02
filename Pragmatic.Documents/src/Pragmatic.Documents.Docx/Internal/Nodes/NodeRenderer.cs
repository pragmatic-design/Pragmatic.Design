using System.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal.Nodes;

/// <summary>Central dispatcher: renders a DocumentNode to OOXML via XmlWriter.</summary>
internal static class NodeRenderer
{
    internal static void Render(XmlWriter w, DocumentNode node, DocxRenderContext ctx)
    {
        switch (node)
        {
            case TextNode text: TextNodeRenderer.Render(w, text); break;
            case HeadingNode heading: HeadingNodeRenderer.Render(w, heading, ctx); break;
            case ParagraphNode para: ParagraphNodeRenderer.Render(w, para, ctx); break;
            case TableNode table: TableNodeRenderer.Render(w, table, ctx); break;
            case ListNode list: ListNodeRenderer.Render(w, list, ctx); break;
            case ImageNode image: ImageNodeRenderer.Render(w, image, ctx); break;
            case HorizontalRuleNode hr: HorizontalRuleNodeRenderer.Render(w); break;
            case SpacerNode spacer: SpacerNodeRenderer.Render(w, spacer); break;
            case PageBreakNode: PageBreakNodeRenderer.Render(w); break;
            case ContainerNode container: ContainerNodeRenderer.Render(w, container, ctx); break;
            case BarcodeNode barcode: BarcodeNodeRenderer.Render(w, barcode, ctx); break;
            case HyperlinkNode link: HyperlinkNodeRenderer.Render(w, link, ctx); break;
            case TocNode toc: TocNodeRenderer.Render(w, toc, ctx); break;
            case FootnoteNode fn: FootnoteNodeRenderer.Render(w, fn, ctx); break;
            case FieldNode field: FieldNodeRenderer.Render(w, field, ctx); break;
            case BookmarkNode bm: BookmarkNodeRenderer.Render(w, bm, ctx); break;
            // Unknown nodes silently ignored
        }
    }

    /// <summary>Render a node as an inline run (for use inside paragraphs).</summary>
    /// <param name="w">The writer.</param>
    /// <param name="node">The inline node.</param>
    /// <param name="ctx">The render context.</param>
    /// <param name="inherited">The enclosing block's style: its run formatting reaches text and links.</param>
    internal static void RenderInline(XmlWriter w, DocumentNode node, DocxRenderContext ctx, NodeStyle? inherited = null)
    {
        switch (node)
        {
            case TextNode text: TextNodeRenderer.RenderRun(w, text, inherited); break;
            case FootnoteNode fn: FootnoteNodeRenderer.RenderInline(w, fn, ctx); break;
            case FieldNode field: FieldNodeRenderer.RenderInline(w, field, ctx); break;
            case HyperlinkNode link: HyperlinkNodeRenderer.RenderInline(w, link, ctx, inherited); break;
            default: Render(w, node, ctx); break;
        }
    }

    /// <summary>Render multiple nodes.</summary>
    internal static void RenderAll(XmlWriter w, IEnumerable<DocumentNode> nodes, DocxRenderContext ctx)
    {
        foreach (var node in nodes)
            Render(w, node, ctx);
    }
}
