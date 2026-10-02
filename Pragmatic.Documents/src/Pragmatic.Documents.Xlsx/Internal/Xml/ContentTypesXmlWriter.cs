using System.Xml;
using Pragmatic.Documents.Ooxml;

namespace Pragmatic.Documents.Xlsx.Internal.Xml;

/// <summary>Writes [Content_Types].xml for the XLSX package.</summary>
internal static class ContentTypesXmlWriter
{
    internal static void WriteTo(Stream stream, int sheetCount, bool hasSharedStrings)
    {
        using var w = XmlWriter.Create(stream, OoxmlXmlSettings.Default);
        w.WriteStartDocument(true);
        w.WriteStartElement("Types", Ns.CT);

        // Default extensions
        WriteDefault(w, "rels", "application/vnd.openxmlformats-package.relationships+xml");
        WriteDefault(w, "xml", "application/xml");

        // Overrides
        WriteOverride(w, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        WriteOverride(w, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");

        if (hasSharedStrings)
            WriteOverride(w, "/xl/sharedStrings.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml");

        WriteOverride(w, "/xl/theme/theme1.xml", "application/vnd.openxmlformats-officedocument.theme+xml");

        for (var i = 1; i <= sheetCount; i++)
            WriteOverride(w, $"/xl/worksheets/sheet{i}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");

        WriteOverride(w, "/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml");
        WriteOverride(w, "/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml");

        w.WriteEndElement();
    }

    private static void WriteDefault(XmlWriter w, string extension, string contentType)
    {
        w.WriteStartElement("Default", Ns.CT);
        w.WriteAttributeString("Extension", extension);
        w.WriteAttributeString("ContentType", contentType);
        w.WriteEndElement();
    }

    private static void WriteOverride(XmlWriter w, string partName, string contentType)
    {
        w.WriteStartElement("Override", Ns.CT);
        w.WriteAttributeString("PartName", partName);
        w.WriteAttributeString("ContentType", contentType);
        w.WriteEndElement();
    }
}
