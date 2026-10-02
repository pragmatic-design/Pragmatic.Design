using System.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal.Nodes;

/// <summary>Renders BarcodeNode. If a resource exists, embeds as image; otherwise renders as text.</summary>
internal static class BarcodeNodeRenderer
{
    internal static void Render(XmlWriter w, BarcodeNode node, DocxRenderContext ctx)
    {
        // Try to find a pre-generated barcode image in resources
        var resourceKey = $"barcode-{SanitizeName(node.Value)}";
        if (ctx.Resources?.TryGetValue(resourceKey, out var imageData) == true)
        {
            var imageNode = new ImageNode
            {
                Source = resourceKey,
                Width = node.Width ?? 30,
                Height = node.Height ?? 30,
                Alt = $"Barcode: {node.Value}"
            };
            ImageNodeRenderer.Render(w, imageNode, ctx);
            return;
        }

        // Fallback: render value as text
        w.WriteStartElement("w", "p", Ns.W);
        w.WriteStartElement("w", "r", Ns.W);
        w.WriteStartElement("w", "t", Ns.W);
        w.WriteString($"[Barcode: {node.Value}]");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static string SanitizeName(string value)
        => new(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
}
