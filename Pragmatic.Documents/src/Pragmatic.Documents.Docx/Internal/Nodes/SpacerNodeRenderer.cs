using System.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal.Nodes;

internal static class SpacerNodeRenderer
{
    internal static void Render(XmlWriter w, SpacerNode node)
    {
        w.WriteStartElement("w", "p", Ns.W);
        w.WriteStartElement("w", "pPr", Ns.W);
        w.WriteStartElement("w", "spacing", Ns.W);
        w.WriteAttributeString("w", "before", Ns.W, Units.MmToTwips(node.Height).ToString());
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }
}
