using System.IO.Compression;
using System.Xml.Linq;
using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Xlsx.Tests;

public class XlsxRendererTests
{
    [Fact]
    public void Render_EmptyWorkbook_ProducesValidZip()
    {
        var model = new SpreadsheetModel { Sheets = [new Sheet { Name = "Empty" }] };
        var bytes = XlsxRenderer.Render(model);

        bytes.Should().NotBeEmpty();
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        zip.Entries.Should().Contain(e => e.FullName == "[Content_Types].xml");
        zip.Entries.Should().Contain(e => e.FullName == "xl/workbook.xml");
        zip.Entries.Should().Contain(e => e.FullName == "xl/worksheets/sheet1.xml");
        zip.Entries.Should().Contain(e => e.FullName == "xl/styles.xml");
    }

    [Fact]
    public void Render_WithStrings_HasSharedStrings()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Data", s => s.Row("Hello", "World"))
            .Build();

        var bytes = XlsxRenderer.Render(model);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        zip.Entries.Should().Contain(e => e.FullName == "xl/sharedStrings.xml");

        var sst = ReadXml(zip, "xl/sharedStrings.xml");
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        sst.Descendants(ns + "t").Select(t => t.Value).Should().Contain("Hello").And.Contain("World");
    }

    [Fact]
    public void Render_Numbers_NotInSharedStrings()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Nums", s => s.Row(42.0, 100))
            .Build();

        var bytes = XlsxRenderer.Render(model);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        // No shared strings needed for numbers
        zip.Entries.Should().NotContain(e => e.FullName == "xl/sharedStrings.xml");

        var sheet = ReadXml(zip, "xl/worksheets/sheet1.xml");
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        sheet.Descendants(ns + "v").Select(v => v.Value).Should().Contain("42").And.Contain("100");
    }

    [Fact]
    public void Render_Formula_WritesFormulaTag()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Calc", s => s
                .Row(10, 20)
                .FormulaRow("=SUM(A1:B1)")
            )
            .Build();

        var bytes = XlsxRenderer.Render(model);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        var sheet = ReadXml(zip, "xl/worksheets/sheet1.xml");
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        sheet.Descendants(ns + "f").Should().ContainSingle()
            .Which.Value.Should().Be("SUM(A1:B1)");
    }

    [Fact]
    public void Render_Workbook_ListsSheets()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Alpha", s => s.Row("A"))
            .Sheet("Beta", s => s.Row("B"))
            .Build();

        var bytes = XlsxRenderer.Render(model);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        var wb = ReadXml(zip, "xl/workbook.xml");
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        var sheets = wb.Descendants(ns + "sheet").ToList();
        sheets.Should().HaveCount(2);
        sheets[0].Attribute("name")!.Value.Should().Be("Alpha");
        sheets[1].Attribute("name")!.Value.Should().Be("Beta");
    }

    [Fact]
    public void Render_FrozenPane_WritesPane()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Frozen", s => s.FreezeRows(1).HeaderRow("A", "B").Row("1", "2"))
            .Build();

        var bytes = XlsxRenderer.Render(model);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        var sheet = ReadXml(zip, "xl/worksheets/sheet1.xml");
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        var pane = sheet.Descendants(ns + "pane").Should().ContainSingle().Subject;
        pane.Attribute("ySplit")!.Value.Should().Be("1");
        pane.Attribute("state")!.Value.Should().Be("frozen");
    }

    [Fact]
    public void Render_MergedCells_WritesMergeCells()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Merge", s => s
                .Merge("A1", "C1")
                .Row("Title", null, null)
                .Row("A", "B", "C")
            )
            .Build();

        var bytes = XlsxRenderer.Render(model);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        var sheet = ReadXml(zip, "xl/worksheets/sheet1.xml");
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        sheet.Descendants(ns + "mergeCell").Should().ContainSingle()
            .Which.Attribute("ref")!.Value.Should().Be("A1:C1");
    }

    [Fact]
    public void Render_ColumnWidths_WritesCols()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Cols", s => s
                .Column(width: 30)
                .Column(width: 15, hidden: true)
                .Row("A", "B")
            )
            .Build();

        var bytes = XlsxRenderer.Render(model);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        var sheet = ReadXml(zip, "xl/worksheets/sheet1.xml");
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        var cols = sheet.Descendants(ns + "col").ToList();
        cols.Should().HaveCount(2);
        cols[0].Attribute("width")!.Value.Should().Be("30");
        cols[1].Attribute("hidden")!.Value.Should().Be("1");
    }

    [Fact]
    public void Render_BoldStyle_WritesFont()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Styled", s => s
                .StyledRow(new Cell { Value = "Bold", Style = new CellStyle { Bold = true, FontSize = 14 } })
            )
            .Build();

        var bytes = XlsxRenderer.Render(model);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        var stylesXml = ReadXml(zip, "xl/styles.xml");
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        stylesXml.Descendants(ns + "b").Should().NotBeEmpty();
        stylesXml.Descendants(ns + "sz").Should().Contain(sz => sz.Attribute("val")!.Value == "14");
    }

    [Fact]
    public void Render_BackgroundColor_WritesFill()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Fill", s => s
                .StyledRow(new Cell { Value = "Yellow", Style = new CellStyle { BackgroundColor = "FFFF00" } })
            )
            .Build();

        var bytes = XlsxRenderer.Render(model);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        var stylesXml = ReadXml(zip, "xl/styles.xml");
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        stylesXml.Descendants(ns + "fgColor").Should().Contain(c => c.Attribute("rgb")!.Value == "FFFFFF00");
    }

    [Fact]
    public void Render_NumberFormat_WritesNumFmt()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("NumFmt", s => s
                .StyledRow(new Cell { Value = 1234.56, Style = new CellStyle { NumberFormat = "#,##0.00" } })
            )
            .Build();

        var bytes = XlsxRenderer.Render(model);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        var stylesXml = ReadXml(zip, "xl/styles.xml");
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        stylesXml.Descendants(ns + "numFmt").Should().Contain(nf => nf.Attribute("formatCode")!.Value == "#,##0.00");
    }

    [Fact]
    public void Render_CoreProperties_WritesMetadata()
    {
        var model = new SpreadsheetBuilder()
            .Title("Test Report")
            .Author("Pragmatic")
            .Sheet("Data", s => s.Row("X"))
            .Build();

        var bytes = XlsxRenderer.Render(model);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        var core = ReadXml(zip, "docProps/core.xml");
        var dc = XNamespace.Get("http://purl.org/dc/elements/1.1/");
        core.Descendants(dc + "title").Should().ContainSingle().Which.Value.Should().Be("Test Report");
        core.Descendants(dc + "creator").Should().ContainSingle().Which.Value.Should().Be("Pragmatic");
    }

    [Fact]
    public void Render_Boolean_WritesBooleanCell()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Bool", s => s
                .Row(new Row([new Cell { Value = true }, new Cell { Value = false }]))
            )
            .Build();

        var bytes = XlsxRenderer.Render(model);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        var sheet = ReadXml(zip, "xl/worksheets/sheet1.xml");
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        var cells = sheet.Descendants(ns + "c").ToList();
        cells[0].Attribute("t")!.Value.Should().Be("b");
        cells[0].Element(ns + "v")!.Value.Should().Be("1");
        cells[1].Element(ns + "v")!.Value.Should().Be("0");
    }

    [Fact]
    public async Task RenderAsync_Works()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Async", s => s.Row("test"))
            .Build();

        var bytes = await XlsxRenderer.RenderAsync(model);
        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public void Render_HiddenRow()
    {
        var model = new SpreadsheetModel
        {
            Sheets = [new Sheet
            {
                Name = "Hidden",
                Rows = [new Row([new Cell { Value = "visible" }]), new Row([new Cell { Value = "hidden" }], Hidden: true)]
            }]
        };

        var bytes = XlsxRenderer.Render(model);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        var sheet = ReadXml(zip, "xl/worksheets/sheet1.xml");
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        var rows = sheet.Descendants(ns + "row").ToList();
        rows[1].Attribute("hidden")!.Value.Should().Be("1");
    }

    [Fact]
    public void Render_Borders_WritesBorders()
    {
        var style = new CellStyle
        {
            BorderTop = new BorderSide("000000", BorderLineStyle.Thin),
            BorderBottom = new BorderSide("FF0000", BorderLineStyle.Thick)
        };
        var model = new SpreadsheetBuilder()
            .Sheet("Borders", s => s.StyledRow(new Cell { Value = "bordered", Style = style }))
            .Build();

        var bytes = XlsxRenderer.Render(model);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        var stylesXml = ReadXml(zip, "xl/styles.xml");
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        // At least 2 borders (default + custom)
        stylesXml.Descendants(ns + "border").Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void Render_DateTimeCell_AppliesDefaultDateNumberFormat()
    {
        var model = new SpreadsheetModel
        {
            Sheets = [new Sheet { Name = "S", Rows = [new Row([new Cell { Value = new DateTime(2026, 1, 15) }])] }],
        };

        using var zip = new ZipArchive(new MemoryStream(XlsxRenderer.Render(model)), ZipArchiveMode.Read);
        var stylesXml = ReadXml(zip, "xl/styles.xml");
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");

        // A default date number-format is synthesized so Excel shows a date, not the serial 46037.
        stylesXml.Descendants(ns + "numFmt")
            .Should().Contain(e => e.Attribute("formatCode")!.Value.Contains("yyyy-mm-dd"));
    }

    [Fact]
    public void Render_DateTimeWithExplicitFormat_DoesNotOverride()
    {
        var model = new SpreadsheetModel
        {
            Sheets = [new Sheet { Name = "S", Rows = [new Row([
                new Cell { Value = new DateTime(2026, 1, 15), Style = new CellStyle { NumberFormat = "dd/MM/yyyy" } }
            ])] }],
        };

        using var zip = new ZipArchive(new MemoryStream(XlsxRenderer.Render(model)), ZipArchiveMode.Read);
        var stylesXml = ReadXml(zip, "xl/styles.xml");
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");

        stylesXml.Descendants(ns + "numFmt").Should().Contain(e => e.Attribute("formatCode")!.Value == "dd/MM/yyyy");
    }

    [Fact]
    public void Render_UnsupportedCellType_EmittedAsStringNotNumber()
    {
        var model = new SpreadsheetModel
        {
            Sheets = [new Sheet { Name = "S", Rows = [new Row([new Cell { Value = Guid.Parse("00000000-0000-0000-0000-000000000001") }])] }],
        };

        using var zip = new ZipArchive(new MemoryStream(XlsxRenderer.Render(model)), ZipArchiveMode.Read);
        var sheetXml = ReadXml(zip, "xl/worksheets/sheet1.xml");
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");

        // The cell must carry t="s" (shared string), never a bare numeric <v>.
        var cell = sheetXml.Descendants(ns + "c").First();
        cell.Attribute("t")!.Value.Should().Be("s");
    }

    private static XDocument ReadXml(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        entry.Should().NotBeNull($"entry '{path}' should exist in ZIP");
        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }
}
