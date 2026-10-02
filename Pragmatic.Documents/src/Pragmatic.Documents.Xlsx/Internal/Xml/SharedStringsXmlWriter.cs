using System.Xml;
using Pragmatic.Documents.Ooxml;

namespace Pragmatic.Documents.Xlsx.Internal.Xml;

/// <summary>Writes xl/sharedStrings.xml with de-duplicated string values.</summary>
internal static class SharedStringsXmlWriter
{
    internal static void WriteTo(Stream stream, SharedStringTable sst)
    {
        using var w = XmlWriter.Create(stream, OoxmlXmlSettings.Default);
        w.WriteStartDocument(true);
        w.WriteStartElement("sst", Ns.SS);
        // OOXML: `count` is the TOTAL number of string-cell references across the workbook,
        // `uniqueCount` is the number of distinct strings in this table.
        w.WriteAttributeString("count", sst.TotalCount.ToString());
        w.WriteAttributeString("uniqueCount", sst.UniqueCount.ToString());

        foreach (var s in sst.Strings)
        {
            w.WriteStartElement("si", Ns.SS);
            w.WriteStartElement("t", Ns.SS);
            // Preserve whitespace
            if (s.Length > 0 && (s[0] == ' ' || s[^1] == ' ' || s.Contains('\n')))
                w.WriteAttributeString("xml", "space", null, "preserve");
            w.WriteString(s);
            w.WriteEndElement(); // t
            w.WriteEndElement(); // si
        }

        w.WriteEndElement(); // sst
    }
}
