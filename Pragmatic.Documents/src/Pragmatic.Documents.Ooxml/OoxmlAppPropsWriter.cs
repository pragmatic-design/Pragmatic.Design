using System.Xml;

namespace Pragmatic.Documents.Ooxml;

/// <summary>Writes docProps/app.xml (extended properties).</summary>
public static class OoxmlAppPropsWriter
{
    private const string Vt = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";

    public static void WriteTo(Stream stream, string applicationName = "Pragmatic.Documents")
    {
        using var w = XmlWriter.Create(stream, OoxmlXmlSettings.Default);
        w.WriteStartDocument(true);
        w.WriteStartElement("Properties", Vt);
        w.WriteElementString("Application", Vt, applicationName);
        w.WriteEndElement();
    }
}
