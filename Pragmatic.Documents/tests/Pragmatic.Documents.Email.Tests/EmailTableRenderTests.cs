using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Email;
using Pragmatic.Email.Model;

namespace Pragmatic.Documents.Email.Tests;

public class EmailTableRenderTests
{
    private readonly IEmailRenderer _renderer = new EmailHtmlRenderer();

    [Fact]
    public void Render_SimpleTable_ProducesHtmlTable()
    {
        var model = new EmailBuilder()
            .FullWidthSection(c => c
                .Table(t => t
                    .HeaderRow("Item", "Qty", "Price")
                    .Row("Widget", "2", "€198.00")
                )
            )
            .Build();

        var html = _renderer.Render(model);

        html.Should().Contain("<table");
        html.Should().Contain("Item");
        html.Should().Contain("Widget");
        html.Should().Contain("€198.00");
        html.Should().Contain("font-weight:bold"); // header is bold
    }

    [Fact]
    public void Render_TableWithBorder_HasBorderStyle()
    {
        var model = new EmailBuilder()
            .FullWidthSection(c => c
                .Table(t => t
                    .BorderColor("#cccccc")
                    .Row("A", "B")
                )
            )
            .Build();

        var html = _renderer.Render(model);

        html.Should().Contain("border:1px solid #cccccc");
    }

    [Fact]
    public void Render_TableWithColumnWidths_SetsWidth()
    {
        var model = new EmailBuilder()
            .FullWidthSection(c => c
                .Table(t => t
                    .Column(width: 300)
                    .Column(width: 100)
                    .Row("Wide", "Narrow")
                )
            )
            .Build();

        var html = _renderer.Render(model);

        html.Should().Contain("width:300px");
        html.Should().Contain("width:100px");
    }

    [Fact]
    public void Render_TableWithAlignment_SetsTextAlign()
    {
        var model = new EmailBuilder()
            .FullWidthSection(c => c
                .Table(t => t
                    .Column(align: EmailTextAlign.Left)
                    .Column(align: EmailTextAlign.Right)
                    .Row("Name", "€99.00")
                )
            )
            .Build();

        var html = _renderer.Render(model);

        html.Should().Contain("text-align:left");
        html.Should().Contain("text-align:right");
    }

    [Fact]
    public void Render_TableCellPadding_Applied()
    {
        var model = new EmailBuilder()
            .FullWidthSection(c => c
                .Table(t => t
                    .CellPadding(12)
                    .Row("Padded")
                )
            )
            .Build();

        var html = _renderer.Render(model);

        html.Should().Contain("cellpadding=\"12\"");
        html.Should().Contain("padding:12px");
    }

    [Fact]
    public void Render_TableRowBackground_Applied()
    {
        var model = new EmailModel
        {
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = [
                new EmailTableNode
                {
                    Rows = [new EmailTableRow
                    {
                        BackgroundColor = "#f8f9fa",
                        Cells = [new EmailTableCell { Content = "Highlighted" }]
                    }]
                }
            ] }] }]
        };

        var html = _renderer.Render(model);

        html.Should().Contain("background-color:#f8f9fa");
    }

    [Fact]
    public void Render_InvoiceTable_FullExample()
    {
        var model = new EmailBuilder()
            .Subject("Invoice")
            .FullWidthSection(c => c
                .Heading("Invoice #2026-001")
                .Table(t => t
                    .Column(width: 300)
                    .Column(width: 80, align: EmailTextAlign.Center)
                    .Column(width: 120, align: EmailTextAlign.Right)
                    .BorderColor("#dddddd")
                    .HeaderRow("Description", "Qty", "Amount")
                    .Row("Room Deluxe (3 nights)", "1", "€450.00")
                    .Row("Breakfast buffet", "3", "€90.00")
                    .Row("Airport transfer", "1", "€60.00")
                )
                .Spacer(10)
                .Text("<b>Total: €600.00</b>")
            )
            .Build();

        var html = _renderer.Render(model);

        html.Should().Contain("Invoice #2026-001");
        html.Should().Contain("Room Deluxe");
        html.Should().Contain("€450.00");
        html.Should().Contain("Airport transfer");
        html.Should().Contain("€600.00");
        html.Should().Contain("border:1px solid #dddddd");
    }

    [Fact]
    public void Render_TableHtmlEncodes_Content()
    {
        var model = new EmailModel
        {
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = [
                new EmailTableNode
                {
                    Rows = [new EmailTableRow { Cells = [new EmailTableCell { Content = "<script>alert('xss')</script>" }] }]
                }
            ] }] }]
        };

        var html = _renderer.Render(model);

        html.Should().Contain("&lt;script&gt;");
        html.Should().NotContain("<script>alert");
    }
}
