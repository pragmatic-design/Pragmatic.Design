using System.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal.Nodes;

/// <summary>Renders ImageNode as w:drawing with inline image.</summary>
internal static class ImageNodeRenderer
{
    internal static void Render(XmlWriter w, ImageNode node, DocxRenderContext ctx)
    {
        var imageData = ResolveImageData(node, ctx);
        var (relId, _) = ctx.AddImage(node.Source, imageData);

        // Default dimensions: 100mm × 75mm if not specified
        var widthEmu = Units.MmToEmu(node.Width ?? 100);
        var heightEmu = Units.MmToEmu(node.Height ?? 75);

        w.WriteStartElement("w", "p", Ns.W);
        DocxStyleMap.WriteParagraphProperties(w, node.Style);

        w.WriteStartElement("w", "r", Ns.W);
        w.WriteStartElement("w", "drawing", Ns.W);
        w.WriteStartElement("wp", "inline", Ns.WP);
        w.WriteAttributeString("distT", "0");
        w.WriteAttributeString("distB", "0");
        w.WriteAttributeString("distL", "0");
        w.WriteAttributeString("distR", "0");

        // Extent (size)
        w.WriteStartElement("wp", "extent", Ns.WP);
        w.WriteAttributeString("cx", widthEmu.ToString());
        w.WriteAttributeString("cy", heightEmu.ToString());
        w.WriteEndElement();

        // DocPr — unique ID per image
        var imageId = ctx.NextImageId();
        w.WriteStartElement("wp", "docPr", Ns.WP);
        w.WriteAttributeString("id", imageId.ToString());
        w.WriteAttributeString("name", node.Alt ?? $"Image{imageId}");
        w.WriteEndElement();

        // Graphic
        w.WriteStartElement("a", "graphic", Ns.A);
        w.WriteStartElement("a", "graphicData", Ns.A);
        w.WriteAttributeString("uri", Ns.PIC);

        w.WriteStartElement("pic", "pic", Ns.PIC);

        // Non-visual properties
        w.WriteStartElement("pic", "nvPicPr", Ns.PIC);
        w.WriteStartElement("pic", "cNvPr", Ns.PIC);
        w.WriteAttributeString("id", imageId.ToString());
        w.WriteAttributeString("name", node.Alt ?? $"Image{imageId}");
        w.WriteEndElement();
        w.WriteStartElement("pic", "cNvPicPr", Ns.PIC);
        w.WriteEndElement();
        w.WriteEndElement(); // nvPicPr

        // Blip fill (image reference)
        w.WriteStartElement("pic", "blipFill", Ns.PIC);
        w.WriteStartElement("a", "blip", Ns.A);
        w.WriteAttributeString("r", "embed", Ns.R, relId);
        w.WriteEndElement();
        w.WriteStartElement("a", "stretch", Ns.A);
        w.WriteStartElement("a", "fillRect", Ns.A);
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement(); // blipFill

        // Shape properties
        w.WriteStartElement("pic", "spPr", Ns.PIC);
        w.WriteStartElement("a", "xfrm", Ns.A);
        w.WriteStartElement("a", "off", Ns.A);
        w.WriteAttributeString("x", "0");
        w.WriteAttributeString("y", "0");
        w.WriteEndElement();
        w.WriteStartElement("a", "ext", Ns.A);
        w.WriteAttributeString("cx", widthEmu.ToString());
        w.WriteAttributeString("cy", heightEmu.ToString());
        w.WriteEndElement();
        w.WriteEndElement(); // xfrm
        w.WriteStartElement("a", "prstGeom", Ns.A);
        w.WriteAttributeString("prst", "rect");
        w.WriteEndElement();
        w.WriteEndElement(); // spPr

        w.WriteEndElement(); // pic:pic
        w.WriteEndElement(); // graphicData
        w.WriteEndElement(); // graphic
        w.WriteEndElement(); // inline
        w.WriteEndElement(); // drawing
        w.WriteEndElement(); // r
        w.WriteEndElement(); // p
    }

    // An image that resolves to nothing is an error, not an omission: dropping it produced a valid
    // document, a successful call and a missing picture that only opening the file revealed.
    private static byte[] ResolveImageData(ImageNode node, DocxRenderContext ctx)
    {
        var source = node.Source;

        if (source.StartsWith("data:", StringComparison.Ordinal))
        {
            var comma = source.IndexOf(',');
            if (comma < 0)
                throw Unresolved(node, "a data: URI needs a ',' between its header and its base64 payload.");
            try
            {
                return Convert.FromBase64String(source[(comma + 1)..]);
            }
            catch (FormatException ex)
            {
                throw Unresolved(node, "its data: payload is not valid base64.", ex);
            }
        }

        var name = source.StartsWith("resource:", StringComparison.Ordinal) ? source[9..] : source;
        if (ctx.Resources is null)
            throw Unresolved(node, $"no resources were passed to the renderer, so there is no '{name}' to find.");
        if (ctx.Resources.TryGetValue(name, out var data))
            return data;

        var available = ctx.Resources.Count == 0
            ? "The resources passed are empty."
            : $"The resources passed are: {string.Join(", ", ctx.Resources.Keys.Order(StringComparer.Ordinal).Select(k => $"'{k}'"))}.";
        throw Unresolved(node, $"no resource is named '{name}'. {available}");
    }

    private static InvalidOperationException Unresolved(ImageNode node, string reason, Exception? inner = null)
    {
        var shown = node.Source.Length > 80 ? node.Source[..80] + "…" : node.Source;
        var alt = node.Alt is null ? "" : $" (alt \"{node.Alt}\")";
        return new InvalidOperationException(
            $"The image '{shown}'{alt} cannot be resolved: {reason} The DOCX renderer embeds bytes it is given " +
            "and does not fetch URLs or read files: pass the image in DocxResources and write its source as " +
            "'resource:name', or inline it as a base64 data: URI.",
            inner);
    }
}
