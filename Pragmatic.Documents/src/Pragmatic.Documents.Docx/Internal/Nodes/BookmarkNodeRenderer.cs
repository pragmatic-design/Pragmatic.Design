using System.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal.Nodes;

/// <summary>Renders BookmarkNode as w:bookmarkStart/End wrapping children.</summary>
internal static class BookmarkNodeRenderer
{
    internal static void Render(XmlWriter w, BookmarkNode node, DocxRenderContext ctx)
    {
        var bmId = ctx.NextBookmarkId().ToString();

        // Block-level children (tables, paragraphs, lists, ...) cannot live inside a single
        // <w:p>: emitting a <w:tbl> inside a run produces invalid OOXML. When any child is
        // block-level, emit bookmarkStart/End as standalone marker paragraphs and render the
        // children as block content between them. Otherwise keep the compact inline form.
        var hasBlockChild = node.Children.Any(IsBlockLevel);

        if (hasBlockChild)
        {
            WriteBookmarkMarker(w, "bookmarkStart", bmId, node.Name);

            foreach (var child in node.Children)
                NodeRenderer.Render(w, child, ctx);

            WriteBookmarkMarker(w, "bookmarkEnd", bmId, name: null);
            return;
        }

        w.WriteStartElement("w", "p", Ns.W);

        w.WriteStartElement("w", "bookmarkStart", Ns.W);
        w.WriteAttributeString("w", "id", Ns.W, bmId);
        w.WriteAttributeString("w", "name", Ns.W, node.Name);
        w.WriteEndElement();

        foreach (var child in node.Children)
            NodeRenderer.RenderInline(w, child, ctx);

        w.WriteStartElement("w", "bookmarkEnd", Ns.W);
        w.WriteAttributeString("w", "id", Ns.W, bmId);
        w.WriteEndElement();

        w.WriteEndElement(); // p
    }

    private static void WriteBookmarkMarker(XmlWriter w, string element, string bmId, string? name)
    {
        // bookmarkStart/End are valid as block-level siblings of paragraphs/tables in the body,
        // so emit them directly (no wrapping <w:p>) to avoid inserting spurious empty paragraphs.
        w.WriteStartElement("w", element, Ns.W);
        w.WriteAttributeString("w", "id", Ns.W, bmId);
        if (name is not null)
            w.WriteAttributeString("w", "name", Ns.W, name);
        w.WriteEndElement();
    }

    private static bool IsBlockLevel(DocumentNode node) => node is
        TableNode or ParagraphNode or ListNode or HeadingNode or ImageNode or
        HorizontalRuleNode or SpacerNode or PageBreakNode or ContainerNode or
        BarcodeNode or TocNode;
}
