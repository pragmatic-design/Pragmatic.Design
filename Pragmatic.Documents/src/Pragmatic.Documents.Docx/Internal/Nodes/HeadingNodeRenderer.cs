using System.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal.Nodes;

/// <summary>Renders HeadingNode as a styled paragraph with bookmark for TOC.</summary>
internal static class HeadingNodeRenderer
{
    internal static void Render(XmlWriter w, HeadingNode node, DocxRenderContext ctx)
    {
        var level = Math.Clamp(node.Level, 1, 6);

        // Use the bookmark name from pre-scan (matches TOC entries).
        // Call NextBookmarkId() once and reuse the value — calling twice would corrupt all subsequent IDs.
        var bmIdInt = ctx.NextBookmarkId();
        var bookmarkName = ctx.GetNextHeadingBookmark() ?? $"_Heading_{bmIdInt}";
        var bmId = bmIdInt.ToString();

        w.WriteStartElement("w", "p", Ns.W);

        // Paragraph properties with heading style
        DocxStyleMap.WriteParagraphProperties(w, node.Style, $"Heading{level}");

        // Bookmark start
        w.WriteStartElement("w", "bookmarkStart", Ns.W);
        w.WriteAttributeString("w", "id", Ns.W, bmId);
        w.WriteAttributeString("w", "name", Ns.W, bookmarkName);
        w.WriteEndElement();

        // Content: prefer Children if set, otherwise use Content string
        if (node.Children is { Count: > 0 })
        {
            foreach (var child in node.Children)
                NodeRenderer.RenderInline(w, child, ctx, node.Style);
        }
        else
        {
            // The heading's own run formatting, over the Heading{level} style: its text has no other
            // place a style could go.
            TextNodeRenderer.RenderRun(w, new TextNode { Content = node.Content }, node.Style);
        }

        // Bookmark end
        w.WriteStartElement("w", "bookmarkEnd", Ns.W);
        w.WriteAttributeString("w", "id", Ns.W, bmId);
        w.WriteEndElement();

        w.WriteEndElement(); // p
    }
}
