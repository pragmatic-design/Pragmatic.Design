using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Xlsx.Tests;

public class XlsxReaderTests
{
    [Fact]
    public void Roundtrip_SimpleStrings()
    {
        var model = new SpreadsheetBuilder()
            .Title("Test")
            .Author("Author")
            .Sheet("Data", s => s.Row("Hello", "World"))
            .Build();

        var bytes = XlsxRenderer.Render(model);
        var read = XlsxReader.Read(bytes);

        read.Title.Should().Be("Test");
        read.Author.Should().Be("Author");
        read.Sheets.Should().HaveCount(1);
        read.Sheets[0].Name.Should().Be("Data");
        read.Sheets[0].Rows.Should().HaveCount(1);
        read.Sheets[0].Rows[0].Cells[0].Value.Should().Be("Hello");
        read.Sheets[0].Rows[0].Cells[1].Value.Should().Be("World");
    }

    [Fact]
    public void Roundtrip_Numbers()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Nums", s => s.Row(42, 3.14))
            .Build();

        var bytes = XlsxRenderer.Render(model);
        var read = XlsxReader.Read(bytes);

        read.Sheets[0].Rows[0].Cells[0].Value.Should().Be(42);
        read.Sheets[0].Rows[0].Cells[1].Value.Should().Be(3.14);
    }

    [Fact]
    public void Roundtrip_Booleans()
    {
        var model = new SpreadsheetModel
        {
            Sheets = [new Sheet
            {
                Name = "Bool",
                Rows = [new Row([new Cell { Value = true }, new Cell { Value = false }])]
            }]
        };

        var bytes = XlsxRenderer.Render(model);
        var read = XlsxReader.Read(bytes);

        read.Sheets[0].Rows[0].Cells[0].Value.Should().Be(true);
        read.Sheets[0].Rows[0].Cells[1].Value.Should().Be(false);
    }

    [Fact]
    public void Roundtrip_Formulas()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Calc", s => s
                .Row(10, 20)
                .FormulaRow("=SUM(A1:B1)")
            )
            .Build();

        var bytes = XlsxRenderer.Render(model);
        var read = XlsxReader.Read(bytes);

        read.Sheets[0].Rows[1].Cells[0].Formula.Should().Be("SUM(A1:B1)");
    }

    [Fact]
    public void Roundtrip_MultipleSheets()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Alpha", s => s.Row("A"))
            .Sheet("Beta", s => s.Row("B"))
            .Sheet("Gamma", s => s.Row("C"))
            .Build();

        var bytes = XlsxRenderer.Render(model);
        var read = XlsxReader.Read(bytes);

        read.Sheets.Should().HaveCount(3);
        read.Sheets[0].Name.Should().Be("Alpha");
        read.Sheets[1].Name.Should().Be("Beta");
        read.Sheets[2].Name.Should().Be("Gamma");
    }

    [Fact]
    public void Roundtrip_FrozenPane()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Frozen", s => s
                .Freeze(2, 1)
                .HeaderRow("A", "B")
                .Row("1", "2")
            )
            .Build();

        var bytes = XlsxRenderer.Render(model);
        var read = XlsxReader.Read(bytes);

        read.Sheets[0].FrozenPane.Should().NotBeNull();
        read.Sheets[0].FrozenPane!.Value.Rows.Should().Be(2);
        read.Sheets[0].FrozenPane!.Value.Columns.Should().Be(1);
    }

    [Fact]
    public void Roundtrip_MergedCells()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Merge", s => s
                .Merge("A1", "C1")
                .Row("Title", null, null)
            )
            .Build();

        var bytes = XlsxRenderer.Render(model);
        var read = XlsxReader.Read(bytes);

        read.Sheets[0].MergedCells.Should().HaveCount(1);
        read.Sheets[0].MergedCells![0].From.Should().Be("A1");
        read.Sheets[0].MergedCells![0].To.Should().Be("C1");
    }

    [Fact]
    public void Roundtrip_ColumnWidths()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Cols", s => s
                .Column(width: 30)
                .Column(width: 15)
                .Row("A", "B")
            )
            .Build();

        var bytes = XlsxRenderer.Render(model);
        var read = XlsxReader.Read(bytes);

        read.Sheets[0].Columns.Should().HaveCount(2);
        read.Sheets[0].Columns![0].Width.Should().Be(30);
        read.Sheets[0].Columns![1].Width.Should().Be(15);
    }

    [Fact]
    public void Roundtrip_HiddenRow()
    {
        var model = new SpreadsheetModel
        {
            Sheets = [new Sheet
            {
                Name = "H",
                Rows = [new Row([new Cell { Value = "visible" }]), new Row([new Cell { Value = "hidden" }], Hidden: true)]
            }]
        };

        var bytes = XlsxRenderer.Render(model);
        var read = XlsxReader.Read(bytes);

        read.Sheets[0].Rows[0].Hidden.Should().BeFalse();
        read.Sheets[0].Rows[1].Hidden.Should().BeTrue();
    }

    [Fact]
    public void Roundtrip_EmptySheet()
    {
        var model = new SpreadsheetModel { Sheets = [new Sheet { Name = "Empty" }] };

        var bytes = XlsxRenderer.Render(model);
        var read = XlsxReader.Read(bytes);

        read.Sheets.Should().HaveCount(1);
        read.Sheets[0].Name.Should().Be("Empty");
        read.Sheets[0].Rows.Should().BeEmpty();
    }

    [Fact]
    public void Roundtrip_FullReport()
    {
        var model = new SpreadsheetBuilder()
            .Title("Report Q1")
            .Author("Pragmatic")
            .Sheet("Riepilogo", s => s
                .Column(width: 30).Column(width: 15).Column(width: 15)
                .FreezeRows(1)
                .Merge("A1", "C1")
                .HeaderRow("Prodotto", "Q1", "Q2")
                .Row("Enterprise", 1050000, 1200000)
                .Row("Professional", 590000, 680000)
                .FormulaRow("Totale", "=SUM(B3:B4)", "=SUM(C3:C4)")
            )
            .Build();

        var bytes = XlsxRenderer.Render(model);
        var read = XlsxReader.Read(bytes);

        read.Title.Should().Be("Report Q1");
        read.Author.Should().Be("Pragmatic");
        read.Sheets[0].FrozenPane!.Value.Rows.Should().Be(1);
        read.Sheets[0].MergedCells.Should().HaveCount(1);
        read.Sheets[0].Rows.Should().HaveCount(4);
        read.Sheets[0].Rows[3].Cells[1].Formula.Should().Be("SUM(B3:B4)");
    }

    [Fact]
    public async Task ReadAsync_Works()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Async", s => s.Row("test"))
            .Build();

        var bytes = XlsxRenderer.Render(model);
        var read = await XlsxReader.ReadAsync(bytes);

        read.Sheets[0].Rows[0].Cells[0].Value.Should().Be("test");
    }
}
