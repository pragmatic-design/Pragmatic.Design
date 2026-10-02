using System.Xml;

namespace Pragmatic.Documents.Docx.Internal.Nodes;

/// <summary>Renders HorizontalRuleNode as a paragraph with bottom border.</summary>
internal static class HorizontalRuleNodeRenderer
{
    internal static void Render(XmlWriter w)
    {
        w.WriteStartElement("w", "p", Ns.W);
        w.WriteStartElement("w", "pPr", Ns.W);
        w.WriteStartElement("w", "pBdr", Ns.W);
        w.WriteStartElement("w", "bottom", Ns.W);
        w.WriteAttributeString("w", "val", Ns.W, "single");
        w.WriteAttributeString("w", "sz", Ns.W, "6");
        w.WriteAttributeString("w", "space", Ns.W, "1");
        w.WriteAttributeString("w", "color", Ns.W, "auto");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement(); // p
    }
}
