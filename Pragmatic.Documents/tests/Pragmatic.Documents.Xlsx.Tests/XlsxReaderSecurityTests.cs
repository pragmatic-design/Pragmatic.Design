using System.IO.Compression;
using System.Text;
using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Xlsx.Tests;

/// <summary>
/// The XLSX reader parses untrusted workbooks, so its XML must reject DTDs (XXE / billion-laughs).
/// </summary>
public class XlsxReaderSecurityTests
{
    [Fact]
    public void Read_WorkbookPartWithDtd_IsRejected()
    {
        // Start from a valid workbook, then inject a DTD into workbook.xml. The reader's hardened
        // XML settings (DtdProcessing.Prohibit, XmlResolver = null) must reject it.
        var valid = XlsxRenderer.Render(new SpreadsheetModel
        {
            Sheets = [new Sheet { Name = "S", Rows = [new Row([new Cell { Value = "x" }])] }],
        });

        var tampered = InjectDtd(valid, "xl/workbook.xml");

        var act = () => XlsxReader.Read(tampered);

        act.Should().Throw<Exception>();
    }

    private static byte[] InjectDtd(byte[] xlsx, string partPath)
    {
        using var input = new MemoryStream(xlsx);
        using var output = new MemoryStream();
        using (var src = new ZipArchive(input, ZipArchiveMode.Read))
        using (var dst = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in src.Entries)
            {
                var outEntry = dst.CreateEntry(entry.FullName);
                using var es = entry.Open();
                using var os = outEntry.Open();
                if (entry.FullName == partPath)
                {
                    var doctype = "<?xml version=\"1.0\"?><!DOCTYPE workbook [ <!ENTITY x \"y\"> ]>";
                    var body = new StreamReader(es).ReadToEnd();
                    // Splice the DOCTYPE in after the XML declaration.
                    var withDtd = doctype + body[(body.IndexOf("?>", StringComparison.Ordinal) + 2)..];
                    var bytes = Encoding.UTF8.GetBytes(withDtd);
                    os.Write(bytes, 0, bytes.Length);
                }
                else
                {
                    es.CopyTo(os);
                }
            }
        }
        return output.ToArray();
    }
}
