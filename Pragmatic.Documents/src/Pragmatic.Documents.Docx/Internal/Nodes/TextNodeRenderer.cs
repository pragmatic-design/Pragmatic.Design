using System.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal.Nodes;

/// <summary>Renders TextNode as a w:p containing a single w:r.</summary>
internal static class TextNodeRenderer
{
    /// <summary>Render as a full paragraph.</summary>
    internal static void Render(XmlWriter w, TextNode node)
    {
        w.WriteStartElement("w", "p", Ns.W);
        DocxStyleMap.WriteParagraphProperties(w, node.Style);
        RenderRun(w, node);
        w.WriteEndElement(); // p
    }

    /// <summary>Render as a run (inline, no wrapping paragraph).</summary>
    /// <param name="w">The writer.</param>
    /// <param name="node">The text.</param>
    /// <param name="inherited">The enclosing block's style, whose run formatting the text's own completes.</param>
    internal static void RenderRun(XmlWriter w, TextNode node, NodeStyle? inherited = null)
    {
        var style = DocxStyleMap.InheritRunFormatting(inherited, node.Style);

        w.WriteStartElement("w", "r", Ns.W);
        if (DocxStyleMap.HasRunProperties(style))
            DocxStyleMap.WriteRunProperties(w, style);
        w.WriteStartElement("w", "t", Ns.W);
        w.WriteAttributeString("xml", "space", null, "preserve");
        w.WriteString(node.Content);
        w.WriteEndElement(); // t
        w.WriteEndElement(); // r
    }
}
