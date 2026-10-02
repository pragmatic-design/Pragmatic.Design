using System.Xml;

namespace Pragmatic.Documents.Ooxml;

/// <summary>Writes docProps/core.xml (Dublin Core metadata).</summary>
public static class OoxmlCorePropsWriter
{
    public static void WriteTo(Stream stream, OoxmlCoreProperties props)
    {
        using var w = XmlWriter.Create(stream, OoxmlXmlSettings.Default);
        w.WriteStartDocument(true);
        w.WriteStartElement("cp", "coreProperties", Ns.CP);
        w.WriteAttributeString("xmlns", "dc", null, Ns.DC);
        w.WriteAttributeString("xmlns", "dcterms", null, Ns.DCTERMS);
        w.WriteAttributeString("xmlns", "xsi", null, Ns.XSI);

        if (props.Title is not null)
            w.WriteElementString("title", Ns.DC, props.Title);
        if (props.Author is not null)
            w.WriteElementString("creator", Ns.DC, props.Author);
        if (props.Subject is not null)
            w.WriteElementString("subject", Ns.DC, props.Subject);
        if (props.Keywords is not null)
            w.WriteElementString("keywords", Ns.CP, props.Keywords);
        if (props.Language is not null)
            w.WriteElementString("language", Ns.DC, props.Language);

        var created = props.CreatedDate ?? DateTimeOffset.UtcNow;
        w.WriteStartElement("dcterms", "created", Ns.DCTERMS);
        w.WriteAttributeString("xsi", "type", Ns.XSI, "dcterms:W3CDTF");
        w.WriteString(created.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        w.WriteEndElement();

        // Use the same timestamp as "created" (rather than an unconditional UtcNow) so that a caller
        // who sets a fixed CreatedDate gets byte-reproducible output; a freshly generated document is
        // created and modified at the same instant anyway.
        w.WriteStartElement("dcterms", "modified", Ns.DCTERMS);
        w.WriteAttributeString("xsi", "type", Ns.XSI, "dcterms:W3CDTF");
        w.WriteString(created.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        w.WriteEndElement();

        w.WriteEndElement();
    }
}
