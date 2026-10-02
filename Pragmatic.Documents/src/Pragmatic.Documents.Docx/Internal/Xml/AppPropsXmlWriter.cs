using System.Xml;

namespace Pragmatic.Documents.Docx.Internal.Xml;

/// <summary>Writes docProps/app.xml (extended properties).</summary>
internal static class AppPropsXmlWriter
{
    private const string Vt = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";

    internal static void WriteTo(Stream stream)
    {
        using var w = XmlWriter.Create(stream, XmlSettings.Default);
        w.WriteStartDocument(true);
        w.WriteStartElement("Properties", Vt);
        w.WriteElementString("Application", Vt, "Pragmatic.Documents.Docx");
        w.WriteEndElement();
    }
}
