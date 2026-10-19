using System.Xml;
using FsCheck;
using FsCheck.Fluent;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Xlsx.Tests;

/// <summary>
///     The XLSX reader parses files an application's users upload (#158, #308). For any input it has four outcomes
///     and no others: a <see cref="SpreadsheetModel" />, an <see cref="InvalidDataException" /> for an archive that is
///     not a ZIP or is damaged, an <see cref="XmlException" /> for a part that is not well-formed XML, or its own
///     refusal, an <see cref="InvalidOperationException" /> whose message starts with <c>XLSX</c>: a limit exceeded, a
///     sheet outside <c>xl/worksheets/</c>, a part the workbook names and the package does not hold.
/// </summary>
/// <remarks>
///     The refusal is told apart by its message because the type alone is also what the BCL throws for a misuse, such
///     as an XML reader driven in the wrong state: that is a defect, and the property must not take it for a refusal.
/// </remarks>
public class XlsxReaderPropertyTests
{
    private const string SheetPart = "xl/worksheets/sheet1.xml";

    private static readonly byte[] Written = XlsxRenderer.Render(new SpreadsheetModel
    {
        Title = "T",
        Author = "A",
        Sheets =
        [
            new Sheet
            {
                Name = "S",
                Rows = [new Row([new Cell { Value = "x" }, new Cell { Value = 1 }, new Cell { Value = true }]), new Row([new Cell { Formula = "A1" }])],
                MergedCells = [new MergeRange("A1", "B1")],
                FrozenPane = new FrozenPane(1, 0),
                Columns = [new Column(Width: 12)],
            },
        ],
    });

    private static readonly string Sheets300 = string.Concat(Enumerable.Range(0, 300).Select(i => $"<sheet name=\"S{i}\" r:id=\"rId1\"/>"));

    // The parts the reader opens, each with what a writer puts in it and what one could put in it instead.
    private static readonly string[] WorkbookFragments =
    [
        "<sheet name=\"A\" r:id=\"rId1\"/>", "<sheet name=\"B\" r:id=\"rId2\"/>", "<sheet r:id=\"rId1\"/>", "<sheet name=\"C\"/>",
        "<sheet name=\"D\" r:id=\"rId9\"/>", "<sheet name=\"E\" r:id=\"\"/>", "<sheets><sheet name=\"F\" r:id=\"rId1\"/></sheets>", Sheets300,
    ];

    private static readonly string[] RelationshipFragments =
    [
        "<Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/>", "<Relationship Id=\"rId2\" Target=\"/xl/worksheets/sheet2.xml\"/>",
        "<Relationship Id=\"rId1\"/>", "<Relationship Target=\"worksheets/sheet1.xml\"/>", "<Relationship Id=\"rId1\" Target=\"\"/>",
        "<Relationship Id=\"rId2\" Target=\"/docProps/core.xml\"/>", "<Relationship Id=\"rId1\" Target=\"worksheets/../../x.xml\"/>",
        "<Relationship Id=\"rId2\" Target=\"worksheets/sheet3.xml\"/>", "<Relationship Id=\"rId1\" Target=\"worksheets\\sheet1.xml\"/>",
    ];

    private static readonly string[] SheetFragments =
    [
        "<sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row></sheetData>",
        "<row><c t=\"s\"><v>-1</v></c><c t=\"s\"><v>99999999999</v></c><c t=\"s\"><v>x</v></c><c t=\"s\"><v>1</v></c></row>",
        "<row><c><v>1e400</v></c><c><v>-0</v></c><c><v>9223372036854775808</v></c><c><v>NaN</v></c><c><v></v></c></row>",
        "<row><c t=\"b\"><v>x</v></c><c t=\"str\"><f>SUM(A1)</f><v>3</v></c><c><f/></c><c t=\"inlineStr\"><is><t>i</t></is></c></row>",
        "<row ht=\"NaN\" hidden=\"1\"/>", "<row ht=\"1e999\"><c/></row>", "<row><row><c><v>1</v></c></row></row>", "<c><v>1</v></c>",
        "<sheetViews><sheetView><pane state=\"frozen\" ySplit=\"1e300\" xSplit=\"-5\"/></sheetView></sheetViews>",
        "<pane state=\"frozen\" ySplit=\"abc\" xSplit=\"NaN\"/>", "<pane state=\"frozen\"/><pane state=\"frozen\" ySplit=\"2\"/>",
        "<cols><col width=\"-1\" hidden=\"1\"/><col/></cols>", "<col width=\"1e309\"><col/></col>",
        "<mergeCells><mergeCell ref=\"A1:B2:C3\"/><mergeCell/><mergeCell ref=\":\"/></mergeCells>", "text",
    ];

    private static readonly string[] SharedStringFragments =
    [
        "<si><t>a</t></si>", "<si><r><t>a</t></r><r><t>b</t></r></si>", "<si/>", "<si><si><t>x</t></si></si>", "<t>loose</t>", "<si>bare</si>",
    ];

    private static readonly string[] CoreFragments = ["<dc:title>T</dc:title>", "<dc:creator/>", "<dc:title><b>x</b></dc:title>", "<dc:creator>A</dc:creator>"];

    // A whole part that is not what its name says.
    private static readonly string[] Raw =
    [
        "", "not xml", "<?xml version=\"1.0\"?>", "<!DOCTYPE x [<!ENTITY a \"b\">]><x>&a;</x>", "<a><b></a>", "<x/>", "﻿<x/>", "<x>\u0001</x>",
    ];

    private static readonly Arbitrary<byte[]> Packages =
        (from workbook in Part(WorkbookFragments, XlsxPackages.Workbook, WorkbookFragments[0])
         from relationships in Part(RelationshipFragments, XlsxPackages.WorkbookRelationships, RelationshipFragments[0])
         from sheet1 in Part(SheetFragments, XlsxPackages.Worksheet)
         from sheet2 in Part(SheetFragments, XlsxPackages.Worksheet)
         from sharedStrings in Part(SharedStringFragments, s => $"<sst xmlns=\"{XlsxPackages.Spreadsheet}\">{s}</sst>", SharedStringFragments[0])
         from core in Part(CoreFragments, s => $"<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">{s}</cp:coreProperties>")
         select XlsxPackages.Package(new[]
             {
                 ("xl/workbook.xml", workbook), ("xl/_rels/workbook.xml.rels", relationships), (SheetPart, sheet1),
                 ("xl/worksheets/sheet2.xml", sheet2), ("xl/sharedStrings.xml", sharedStrings), ("docProps/core.xml", core),
             }
             .Where(part => part.Item2 is not null)
             .Select(part => KeyValuePair.Create(part.Item1, part.Item2!))))
        .ToArbitrary();

    private static readonly Arbitrary<byte[]> Damaged =
        (from edits in Gen.Choose(0, Written.Length - 1).Zip(ArbMap.Default.GeneratorFor<byte>()).ListOf()
         from length in Gen.Frequency((3, Gen.Constant(Written.Length)), (1, Gen.Choose(0, Written.Length)))
         select Damage(edits, length))
        .ToArbitrary();

    [Fact]
    public void Any_bytes_read_to_a_declared_outcome()
        => Prop.ForAll(ArbMap.Default.ArbFor<byte[]>(), ReadIsDeclared).QuickCheckThrowOnFailure();

    [Fact]
    public void Any_damage_to_a_written_workbook_reads_to_a_declared_outcome()
        => Prop.ForAll(Damaged, ReadIsDeclared).QuickCheckThrowOnFailure();

    [Fact]
    public void Any_parts_read_to_a_declared_outcome()
        => Prop.ForAll(Packages, ReadIsDeclared).Check(Config.QuickThrowOnFailure.WithMaxTest(500));

    [Fact]
    public void A_written_workbook_reads_back_its_values()
    {
        var values = Gen.OneOf(
            ArbMap.Default.GeneratorFor<int>().Select(i => (object)i),
            ArbMap.Default.GeneratorFor<bool>().Select(b => (object)b),
            Gen.Elements("a", "Ab c", "<&>\"'", "x1", "è").Select(s => (object)s));
        var sheets = values.ListOf().ListOf().Select(rows => rows.Select(row => row.ToList()).ToList());

        Prop.ForAll(sheets.ToArbitrary(), rows =>
        {
            var model = new SpreadsheetModel
            {
                Sheets = [new Sheet { Name = "S", Rows = [.. rows.Select(row => new Row([.. row.Select(value => new Cell { Value = value })]))] }],
            };

            var read = XlsxReader.Read(XlsxRenderer.Render(model)).Sheets.Single().Rows;
            return read.Count == rows.Count
                   && read.Zip(rows).All(pair => pair.First.Cells.Select(cell => cell.Value).SequenceEqual(pair.Second));
        }).QuickCheckThrowOnFailure();
    }

    private static bool ReadIsDeclared(byte[] package)
    {
        try
        {
            return XlsxReader.Read(package) is not null;
        }
        catch (InvalidDataException)
        {
            return true;
        }
        catch (XmlException)
        {
            return true;
        }
        catch (InvalidOperationException e)
        {
            return e.Message.StartsWith("XLSX ", StringComparison.Ordinal);
        }
    }

    // Absent, or what a writer puts there, or the part's root around some of its fragments, or something else
    // entirely. The workbook and its relationships are usually a writer's: one wrong sheet refuses the package before
    // any worksheet is read, so without them the worksheet fragments would rarely be reached.
    private static Gen<string?> Part(string[] fragments, Func<string, string> root, string? usual = null)
        => Gen.Frequency(
            (1, Gen.Constant<string?>(null)),
            (usual is null ? 0 : 6, Gen.Constant<string?>(usual is null ? null : root(usual))),
            (4, Gen.Elements(fragments).ListOf().Select(chosen => (string?)root(string.Concat(chosen)))),
            (1, Gen.Elements(Raw).Select(raw => (string?)raw)));

    private static byte[] Damage(IEnumerable<(int Position, byte Value)> edits, int length)
    {
        var bytes = (byte[])Written.Clone();
        foreach (var (position, value) in edits)
            bytes[position] = value;
        return bytes[..length];
    }
}
