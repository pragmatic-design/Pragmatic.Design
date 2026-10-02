using System.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal.Nodes;

/// <summary>Renders ListNode as paragraphs with w:numPr for numbering.</summary>
internal static class ListNodeRenderer
{
    internal static void Render(XmlWriter w, ListNode node, DocxRenderContext ctx, int level = 0)
    {
        // Register a numbering definition if this is a top-level list
        int numId;
        if (level == 0)
        {
            numId = ctx.NextNumberingId();
            var abstractNumId = numId;
            ctx.NumberingDefs.Add(new NumberingDef(numId, abstractNumId, node.Ordered));
        }
        else
        {
            // Nested: reuse the last numbering ID (create a new definition if no parent was registered).
            if (ctx.NumberingDefs.Count > 0)
            {
                numId = ctx.NumberingDefs[^1].NumId;
            }
            else
            {
                numId = ctx.NextNumberingId();
                ctx.NumberingDefs.Add(new NumberingDef(numId, numId, node.Ordered));
            }
        }

        foreach (var item in node.Items)
        {
            w.WriteStartElement("w", "p", Ns.W);

            // Paragraph properties with numbering
            w.WriteStartElement("w", "pPr", Ns.W);
            w.WriteStartElement("w", "numPr", Ns.W);
            w.WriteStartElement("w", "ilvl", Ns.W);
            w.WriteAttributeString("w", "val", Ns.W, level.ToString());
            w.WriteEndElement();
            w.WriteStartElement("w", "numId", Ns.W);
            w.WriteAttributeString("w", "val", Ns.W, numId.ToString());
            w.WriteEndElement();
            w.WriteEndElement(); // numPr
            w.WriteEndElement(); // pPr

            // Item content as inline runs
            foreach (var child in item.Content)
                NodeRenderer.RenderInline(w, child, ctx);

            w.WriteEndElement(); // p

            // Nested sub-list
            if (item.SubList is not null)
                Render(w, item.SubList, ctx, level + 1);
        }
    }
}
