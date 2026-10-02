using System.Xml;

namespace Pragmatic.Documents.Docx.Internal.Nodes;

internal static class PageBreakNodeRenderer
{
    internal static void Render(XmlWriter w)
    {
        w.WriteStartElement("w", "p", Ns.W);
        w.WriteStartElement("w", "r", Ns.W);
        w.WriteStartElement("w", "br", Ns.W);
        w.WriteAttributeString("w", "type", Ns.W, "page");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }
}
