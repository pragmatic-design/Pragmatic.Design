using System.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal.Xml;

/// <summary>Writes docProps/core.xml (Dublin Core metadata).</summary>
internal static class CorePropsXmlWriter
{
    internal static void WriteTo(Stream stream, DocumentModel model)
    {
        using var w = XmlWriter.Create(stream, XmlSettings.Default);
        w.WriteStartDocument(true);
        w.WriteStartElement("cp", "coreProperties", Ns.CP);
        w.WriteAttributeString("xmlns", "dc", null, Ns.DC);
        w.WriteAttributeString("xmlns", "dcterms", null, Ns.DCTERMS);
        w.WriteAttributeString("xmlns", "xsi", null, Ns.XSI);

        if (model.Title is not null)
            w.WriteElementString("title", Ns.DC, model.Title);
        if (model.Author is not null)
            w.WriteElementString("creator", Ns.DC, model.Author);
        if (model.Subject is not null)
            w.WriteElementString("subject", Ns.DC, model.Subject);
        if (model.Keywords is not null)
            w.WriteElementString("keywords", Ns.CP, model.Keywords);
        if (model.Language is not null)
            w.WriteElementString("language", Ns.DC, model.Language);

        var created = model.CreatedDate ?? DateTimeOffset.UtcNow;
        w.WriteStartElement("dcterms", "created", Ns.DCTERMS);
        w.WriteAttributeString("xsi", "type", Ns.XSI, "dcterms:W3CDTF");
        w.WriteString(created.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        w.WriteEndElement();

        w.WriteStartElement("dcterms", "modified", Ns.DCTERMS);
        w.WriteAttributeString("xsi", "type", Ns.XSI, "dcterms:W3CDTF");
        w.WriteString(DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        w.WriteEndElement();

        w.WriteEndElement();
    }
}
