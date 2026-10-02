using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Spreadsheet.Tests;

public class SpreadsheetBuilderTests
{
    [Fact]
    public void Builder_EmptyWorkbook()
    {
        var model = new SpreadsheetBuilder()
            .Title("Empty")
            .Author("Test")
            .Build();

        model.Title.Should().Be("Empty");
        model.Author.Should().Be("Test");
        model.Sheets.Should().BeEmpty();
    }

    [Fact]
    public void Builder_SingleSheet_WithColumns()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Data", s => s
                .Column(width: 30)
                .Column(width: 15)
                .HeaderRow("Name", "Total")
                .Row("Widget", 99.0)
            )
            .Build();

        model.Sheets.Should().HaveCount(1);
        var sheet = model.Sheets[0];
        sheet.Name.Should().Be("Data");
        sheet.Columns.Should().HaveCount(2);
        sheet.Columns![0].Width.Should().Be(30);
        sheet.Rows.Should().HaveCount(2);

        // Header row has bold style
        sheet.Rows[0].Cells[0].Style!.Bold.Should().BeTrue();
        sheet.Rows[0].Cells[0].Value.Should().Be("Name");

        // Data row
        sheet.Rows[1].Cells[0].Value.Should().Be("Widget");
        sheet.Rows[1].Cells[1].Value.Should().Be(99.0);
    }

    [Fact]
    public void Builder_FormulaRow_ParsesEquals()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Calc", s => s
                .Row(10, 20, 30)
                .FormulaRow("Totale", "=SUM(B1:B1)", "=SUM(C1:C1)")
            )
            .Build();

        var formulaRow = model.Sheets[0].Rows[1];
        formulaRow.Cells[0].Value.Should().Be("Totale");
        formulaRow.Cells[0].Formula.Should().BeNull();
        formulaRow.Cells[1].Formula.Should().Be("SUM(B1:B1)");
        formulaRow.Cells[1].Value.Should().BeNull();
    }

    [Fact]
    public void Builder_FreezeRows()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Frozen", s => s
                .FreezeRows(1)
                .HeaderRow("A", "B")
                .Row("1", "2")
            )
            .Build();

        model.Sheets[0].FrozenPane.Should().NotBeNull();
        model.Sheets[0].FrozenPane!.Value.Rows.Should().Be(1);
        model.Sheets[0].FrozenPane!.Value.Columns.Should().Be(0);
    }

    [Fact]
    public void Builder_FreezeRowsAndColumns()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Frozen", s => s
                .Freeze(2, 1)
                .HeaderRow("A", "B")
            )
            .Build();

        model.Sheets[0].FrozenPane!.Value.Rows.Should().Be(2);
        model.Sheets[0].FrozenPane!.Value.Columns.Should().Be(1);
    }

    [Fact]
    public void Builder_MergedCells()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Merge", s => s
                .Merge("A1", "C1")
                .Row("Title merged", null, null)
                .Row("A", "B", "C")
            )
            .Build();

        model.Sheets[0].MergedCells.Should().HaveCount(1);
        model.Sheets[0].MergedCells![0].From.Should().Be("A1");
        model.Sheets[0].MergedCells![0].To.Should().Be("C1");
    }

    [Fact]
    public void Builder_MultipleSheets()
    {
        var model = new SpreadsheetBuilder()
            .Title("Multi")
            .Sheet("Sheet1", s => s.Row("A"))
            .Sheet("Sheet2", s => s.Row("B"))
            .Sheet("Sheet3", s => s.Row("C"))
            .Build();

        model.Sheets.Should().HaveCount(3);
        model.Sheets[0].Name.Should().Be("Sheet1");
        model.Sheets[1].Name.Should().Be("Sheet2");
        model.Sheets[2].Name.Should().Be("Sheet3");
    }

    [Fact]
    public void Builder_StyledRow()
    {
        var style = new CellStyle { NumberFormat = "#,##0.00", HorizontalAlign = HorizontalAlign.Right };
        var model = new SpreadsheetBuilder()
            .Sheet("Styled", s => s
                .StyledRow(
                    new Cell { Value = "Total", Style = new CellStyle { Bold = true } },
                    new Cell { Value = 1234.56m, Style = style }
                )
            )
            .Build();

        var row = model.Sheets[0].Rows[0];
        row.Cells[0].Style!.Bold.Should().BeTrue();
        row.Cells[1].Style!.NumberFormat.Should().Be("#,##0.00");
        row.Cells[1].Value.Should().Be(1234.56m);
    }

    [Fact]
    public void Builder_PreBuiltSheet()
    {
        var sheet = new Sheet
        {
            Name = "Pre-built",
            Rows = [new Row([new Cell { Value = "Hello" }])]
        };

        var model = new SpreadsheetBuilder()
            .Sheet(sheet)
            .Build();

        model.Sheets[0].Name.Should().Be("Pre-built");
    }

    [Fact]
    public void Builder_FullReportExample()
    {
        var model = new SpreadsheetBuilder()
            .Title("Report Q1")
            .Author("Pragmatic")
            .Sheet("Riepilogo", s => s
                .Column(width: 30).Column(width: 15).Column(width: 15)
                .FreezeRows(1)
                .HeaderRow("Prodotto", "Q1", "Q2")
                .Row("Enterprise", 1050000, 1200000)
                .Row("Professional", 590000, 680000)
                .FormulaRow("Totale", "=SUM(B2:B3)", "=SUM(C2:C3)")
            )
            .Build();

        model.Title.Should().Be("Report Q1");
        model.Sheets.Should().HaveCount(1);
        var sheet = model.Sheets[0];
        sheet.Rows.Should().HaveCount(4);
        sheet.FrozenPane!.Value.Rows.Should().Be(1);
        sheet.Columns.Should().HaveCount(3);

        // Formula row
        sheet.Rows[3].Cells[1].Formula.Should().Be("SUM(B2:B3)");
    }

    [Fact]
    public void Build_ThenMutateBuilder_DoesNotAffectBuiltModel()
    {
        var builder = new SpreadsheetBuilder()
            .Sheet("A", s => s.Row("one"));

        var model = builder.Build();

        builder.Sheet("B", s => s.Row("two"));

        model.Sheets.Should().ContainSingle();
    }

    [Fact]
    public void Merge_ValidA1References_Succeeds()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("S", s => s.Merge("A1", "C1").Row("x"))
            .Build();

        model.Sheets[0].MergedCells.Should().ContainSingle()
            .Which.Should().Be(new MergeRange("A1", "C1"));
    }

    [Theory]
    [InlineData("", "B1")]
    [InlineData("1A", "B1")]
    [InlineData("A1", "B0")]
    [InlineData("A1", "notaref")]
    public void Merge_InvalidA1Reference_ThrowsFormatException(string from, string to)
    {
        var act = () => new SpreadsheetBuilder()
            .Sheet("S", s => s.Merge(from, to))
            .Build();

        act.Should().Throw<FormatException>();
    }
}
