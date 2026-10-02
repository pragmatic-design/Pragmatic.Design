namespace Pragmatic.Email.Model;

/// <summary>Fluent builder for <see cref="EmailTableNode"/>.</summary>
public sealed class EmailTableBuilder
{
    private readonly List<EmailTableColumn> _columns = [];
    private EmailTableRow? _header;
    private readonly List<EmailTableRow> _rows = [];
    private string? _borderColor;
    private int _cellPadding = 8;

    public EmailTableBuilder Column(int? width = null, EmailTextAlign align = EmailTextAlign.Left)
    {
        _columns.Add(new EmailTableColumn { Width = width, Align = align });
        return this;
    }

    public EmailTableBuilder BorderColor(string color) { _borderColor = color; return this; }
    public EmailTableBuilder CellPadding(int padding) { _cellPadding = padding; return this; }

    public EmailTableBuilder HeaderRow(params string[] cells)
    {
        _header = new EmailTableRow
        {
            Cells = cells.Select(c => new EmailTableCell { Content = c, Bold = true }).ToList()
        };
        return this;
    }

    public EmailTableBuilder Row(params string[] cells)
    {
        _rows.Add(new EmailTableRow
        {
            Cells = cells.Select(c => new EmailTableCell { Content = c }).ToList()
        });
        return this;
    }

    public EmailTableBuilder Row(EmailTableRow row) { _rows.Add(row); return this; }

    internal EmailTableNode Build() => new()
    {
        Columns = [.._columns],
        Header = _header,
        Rows = [.._rows],
        BorderColor = _borderColor,
        CellPadding = _cellPadding
    };
}
