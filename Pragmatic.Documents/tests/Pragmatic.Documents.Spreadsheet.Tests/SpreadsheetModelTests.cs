using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Spreadsheet.Tests;

public class SpreadsheetModelTests
{
    [Fact]
    public void EmptyModel_HasDefaults()
    {
        var model = new SpreadsheetModel();

        model.Title.Should().BeNull();
        model.Author.Should().BeNull();
        model.Sheets.Should().BeEmpty();
    }

    [Fact]
    public void Model_WithProperties_PreservesValues()
    {
        var sheet = new Sheet { Name = "Data" };
        var model = new SpreadsheetModel
        {
            Title = "Report",
            Author = "Test",
            Sheets = [sheet]
        };

        model.Title.Should().Be("Report");
        model.Author.Should().Be("Test");
        model.Sheets.Should().HaveCount(1);
        model.Sheets[0].Name.Should().Be("Data");
    }

    [Fact]
    public void Sheet_WithAllProperties()
    {
        var sheet = new Sheet
        {
            Name = "Sheet1",
            Columns = [new Column(Width: 20), new Column(Hidden: true)],
            Rows = [new Row([new Cell { Value = "A" }])],
            FrozenPane = new FrozenPane(1, 0),
            MergedCells = [new MergeRange("A1", "C1")]
        };

        sheet.Name.Should().Be("Sheet1");
        sheet.Columns.Should().HaveCount(2);
        sheet.Columns![1].Hidden.Should().BeTrue();
        sheet.Rows.Should().HaveCount(1);
        sheet.FrozenPane!.Value.Rows.Should().Be(1);
        sheet.MergedCells.Should().HaveCount(1);
    }

    [Fact]
    public void Cell_WithValue()
    {
        var cell = new Cell { Value = 42.5, Style = new CellStyle { Bold = true } };

        cell.Value.Should().Be(42.5);
        cell.Formula.Should().BeNull();
        cell.Style!.Bold.Should().BeTrue();
    }

    [Fact]
    public void Cell_WithFormula()
    {
        var cell = new Cell { Formula = "SUM(A1:A10)", Value = 100.0 };

        cell.Formula.Should().Be("SUM(A1:A10)");
        cell.Value.Should().Be(100.0);
    }

    [Fact]
    public void CellStyle_AllProperties()
    {
        var style = new CellStyle
        {
            FontFamily = "Arial",
            FontSize = 12,
            Bold = true,
            Italic = false,
            FontColor = "FF0000",
            BackgroundColor = "FFFF00",
            HorizontalAlign = HorizontalAlign.Center,
            VerticalAlign = VerticalAlign.Bottom,
            WrapText = true,
            NumberFormat = "#,##0.00",
            BorderTop = new BorderSide("000000", BorderLineStyle.Thin),
            BorderBottom = new BorderSide("0000FF", BorderLineStyle.Thick),
            BorderLeft = new BorderSide(),
            BorderRight = new BorderSide("FF0000", BorderLineStyle.Dashed)
        };

        style.FontFamily.Should().Be("Arial");
        style.FontSize.Should().Be(12);
        style.Bold.Should().BeTrue();
        style.FontColor.Should().Be("FF0000");
        style.BackgroundColor.Should().Be("FFFF00");
        style.HorizontalAlign.Should().Be(HorizontalAlign.Center);
        style.VerticalAlign.Should().Be(VerticalAlign.Bottom);
        style.WrapText.Should().BeTrue();
        style.NumberFormat.Should().Be("#,##0.00");
        style.BorderTop!.Style.Should().Be(BorderLineStyle.Thin);
        style.BorderBottom!.Style.Should().Be(BorderLineStyle.Thick);
        style.BorderLeft!.Color.Should().Be("000000");
        style.BorderRight!.Style.Should().Be(BorderLineStyle.Dashed);
    }

    [Fact]
    public void BorderSide_Defaults()
    {
        var border = new BorderSide();

        border.Color.Should().Be("000000");
        border.Style.Should().Be(BorderLineStyle.Thin);
    }

    [Fact]
    public void FrozenPane_Defaults()
    {
        var pane = new FrozenPane();

        pane.Rows.Should().Be(0);
        pane.Columns.Should().Be(0);
    }

    [Fact]
    public void MergeRange_Values()
    {
        var range = new MergeRange("A1", "C3");

        range.From.Should().Be("A1");
        range.To.Should().Be("C3");
    }

    [Fact]
    public void Row_WithHeight()
    {
        var row = new Row([new Cell { Value = "test" }], Height: 25.0, Hidden: true);

        row.Height.Should().Be(25.0);
        row.Hidden.Should().BeTrue();
        row.Cells.Should().HaveCount(1);
    }
}
