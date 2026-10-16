using System.IO.Compression;
using System.Text;
using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Xlsx.Tests;

/// <summary>
///     The worksheet a workbook relationship names is read when the part is inside <c>xl/worksheets/</c>,
///     however the target is spelled, and refused when it is not (#258).
/// </summary>
/// <remarks>
///     <para>
///         OPC allows a relationship target to be absolute from the package root or relative to the source
///         part. Excel writes <c>worksheets/sheet1.xml</c>; ClosedXML writes <c>/xl/worksheets/sheet1.xml</c>.
///         Both name the same part, and a reader that guards the spelling instead of the part refuses one of
///         them.
///     </para>
///     <para>
///         Each case starts from a workbook this library wrote and rewrites only the target in
///         <c>xl/_rels/workbook.xml.rels</c>, so the package is otherwise valid: a refusal is the guard's, not
///         a broken file's.
///     </para>
/// </remarks>
public class XlsxReaderWorksheetTargetTests
{
    private const string WrittenTarget = "Target=\"worksheets/sheet1.xml\"";

    [Fact]
    public void Read_RelativeTarget_ReadsTheSheet()
    {
        var sheet = XlsxReader.Read(WithTarget("worksheets/sheet1.xml")).Sheets.Single();

        sheet.Rows[0].Cells[0].Value.Should().Be("x");
    }

    [Fact]
    public void Read_AbsoluteTarget_ReadsTheSheet()
    {
        var sheet = XlsxReader.Read(WithTarget("/xl/worksheets/sheet1.xml")).Sheets.Single();

        sheet.Rows[0].Cells[0].Value.Should().Be("x", "an absolute target names the same part a relative one does");
    }

    [Theory]
    [InlineData("/docProps/core.xml")]
    [InlineData("/_rels/.rels")]
    [InlineData("../docProps/core.xml")]
    [InlineData("/xl/worksheets/../../docProps/core.xml")]
    [InlineData("worksheets/../../docProps/core.xml")]
    [InlineData("\\xl\\worksheets\\sheet1.xml")]
    [InlineData("//server/share/sheet1.xml")]
    public void Read_TargetOutsideTheWorksheets_IsRefused(string target)
    {
        var act = () => XlsxReader.Read(WithTarget(target));

        act.Should().Throw<InvalidOperationException>();
    }

    private static byte[] WithTarget(string target)
    {
        var valid = XlsxRenderer.Render(new SpreadsheetModel
        {
            Sheets = [new Sheet { Name = "S", Rows = [new Row([new Cell { Value = "x" }])] }],
        });

        using var input = new MemoryStream(valid);
        using var output = new MemoryStream();
        using (var src = new ZipArchive(input, ZipArchiveMode.Read))
        using (var dst = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in src.Entries)
            {
                var outEntry = dst.CreateEntry(entry.FullName);
                using var es = entry.Open();
                using var os = outEntry.Open();
                if (entry.FullName == "xl/_rels/workbook.xml.rels")
                {
                    var rels = new StreamReader(es).ReadToEnd();
                    if (!rels.Contains(WrittenTarget, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"The writer no longer writes {WrittenTarget}; the rewrite would change nothing.");

                    var bytes = Encoding.UTF8.GetBytes(rels.Replace(WrittenTarget, $"Target=\"{target}\"", StringComparison.Ordinal));
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
