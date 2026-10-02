using System.Xml;
using Pragmatic.Documents.Ooxml;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Xlsx.Internal.Xml;

/// <summary>Writes xl/workbook.xml listing all sheets.</summary>
internal static class WorkbookXmlWriter
{
    internal static void WriteTo(Stream stream, SpreadsheetModel model, IReadOnlyList<string> sheetRelIds)
    {
        using var w = XmlWriter.Create(stream, OoxmlXmlSettings.Default);
        w.WriteStartDocument(true);
        w.WriteStartElement("workbook", Ns.SS);
        w.WriteAttributeString("xmlns", "r", null, Ns.R);

        w.WriteStartElement("sheets", Ns.SS);
        for (var i = 0; i < model.Sheets.Count; i++)
        {
            w.WriteStartElement("sheet", Ns.SS);
            w.WriteAttributeString("name", model.Sheets[i].Name);
            w.WriteAttributeString("sheetId", (i + 1).ToString());
            w.WriteAttributeString("r", "id", Ns.R, sheetRelIds[i]);
            w.WriteEndElement();
        }
        w.WriteEndElement(); // sheets

        w.WriteEndElement(); // workbook
    }
}
