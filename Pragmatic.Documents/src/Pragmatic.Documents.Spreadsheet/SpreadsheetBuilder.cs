namespace Pragmatic.Documents.Spreadsheet;

/// <summary>
/// Fluent builder for constructing <see cref="SpreadsheetModel"/> instances.
/// </summary>
public sealed class SpreadsheetBuilder
{
    private string? _title;
    private string? _author;
    private readonly List<Sheet> _sheets = [];

    public SpreadsheetBuilder Title(string title) { _title = title; return this; }
    public SpreadsheetBuilder Author(string author) { _author = author; return this; }

    /// <summary>Add a sheet using a sheet builder.</summary>
    public SpreadsheetBuilder Sheet(string name, Action<SheetBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new SheetBuilder(name);
        configure(builder);
        _sheets.Add(builder.Build());
        return this;
    }

    /// <summary>Add a pre-built sheet.</summary>
    public SpreadsheetBuilder Sheet(Sheet sheet)
    {
        _sheets.Add(sheet);
        return this;
    }

    public SpreadsheetModel Build() => new()
    {
        Title = _title,
        Author = _author,
        // Defensive copy: the returned model must not alias the builder's mutable backing list.
        Sheets = [.._sheets]
    };
}
