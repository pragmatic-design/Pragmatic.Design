using System.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal.Nodes;

/// <summary>Renders HyperlinkNode as w:hyperlink.</summary>
internal static class HyperlinkNodeRenderer
{
    internal static void Render(XmlWriter w, HyperlinkNode node, DocxRenderContext ctx)
    {
        w.WriteStartElement("w", "p", Ns.W);
        DocxStyleMap.WriteParagraphProperties(w, node.Style);
        RenderInline(w, node, ctx);
        w.WriteEndElement(); // p
    }

    /// <param name="w">The writer.</param>
    /// <param name="node">The link.</param>
    /// <param name="ctx">The render context.</param>
    /// <param name="inherited">The enclosing block's style, which the link's own completes for its text.</param>
    internal static void RenderInline(XmlWriter w, HyperlinkNode node, DocxRenderContext ctx, NodeStyle? inherited = null)
    {
        var linkStyle = DocxStyleMap.InheritRunFormatting(inherited, node.Style);

        var isInternal = node.Href.StartsWith('#');
        // Only http/https/mailto/tel are allowed as external targets. A javascript:/file:/UNC target
        // is dropped: the text is still rendered, but no clickable link (or rel) is created.
        var isSafeExternal = !isInternal && IsAllowedScheme(node.Href);

        w.WriteStartElement("w", "hyperlink", Ns.W);

        if (isInternal)
        {
            w.WriteAttributeString("w", "anchor", Ns.W, node.Href[1..]);
        }
        else if (isSafeExternal)
        {
            var relId = ctx.ActiveRels.Add(Ns.RelHyperlink, node.Href, external: true);
            w.WriteAttributeString("r", "id", Ns.R, relId);
        }
        // else: unsafe external scheme — emit the children as plain text with no link target.

        // Render children with the Hyperlink character style and the run formatting that cascades to
        // them, the text's own style included.
        foreach (var child in node.Children)
        {
            if (child is TextNode text)
            {
                var style = DocxStyleMap.InheritRunFormatting(linkStyle, text.Style);

                w.WriteStartElement("w", "r", Ns.W);
                DocxStyleMap.WriteRunProperties(w, DocxStyleMap.HasRunProperties(style) ? style : null, "Hyperlink");
                w.WriteStartElement("w", "t", Ns.W);
                w.WriteAttributeString("xml", "space", null, "preserve");
                w.WriteString(text.Content);
                w.WriteEndElement();
                w.WriteEndElement();
            }
            else
            {
                NodeRenderer.RenderInline(w, child, ctx, linkStyle);
            }
        }

        w.WriteEndElement(); // hyperlink
    }

    private static bool IsAllowedScheme(string href)
    {
        var trimmed = href.TrimStart();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);
    }
}
