namespace Pragmatic.Documents.Model;

/// <summary>Fluent builder for <see cref="TableNode"/>.</summary>
public sealed class TableBuilder
{
    private readonly List<TableColumn> _columns = [];
    private TableRow? _header;
    private readonly List<TableRow> _rows = [];
    private bool _repeatHeader = true;

    public TableBuilder Column(double? width = null, TextAlign? align = null)
    {
        _columns.Add(new TableColumn { Width = width, Align = align });
        return this;
    }

    public TableBuilder HeaderRow(params string[] cells)
    {
        _header = new TableRow
        {
            Cells = cells.Select(c => new TableCell
            {
                Content = [new TextNode { Content = c, Style = new NodeStyle { FontWeight = FontWeight.Bold } }]
            }).ToList()
        };
        return this;
    }

    public TableBuilder Row(params string[] cells)
    {
        _rows.Add(new TableRow
        {
            Cells = cells.Select(c => new TableCell
            {
                Content = [new TextNode { Content = c }]
            }).ToList()
        });
        return this;
    }

    public TableBuilder Row(TableRow row) { _rows.Add(row); return this; }
    public TableBuilder NoRepeatHeader() { _repeatHeader = false; return this; }

    internal TableNode Build() => new()
    {
        Columns = _columns,
        Header = _header,
        Rows = _rows,
        RepeatHeader = _repeatHeader
    };
}
