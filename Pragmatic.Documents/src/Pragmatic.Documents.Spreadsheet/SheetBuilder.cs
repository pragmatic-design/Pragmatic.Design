namespace Pragmatic.Documents.Spreadsheet;

/// <summary>
/// Fluent builder for constructing <see cref="Sheet"/> instances.
/// </summary>
public sealed class SheetBuilder
{
    private readonly string _name;
    private readonly List<Column> _columns = [];
    private readonly List<Row> _rows = [];
    private readonly List<MergeRange> _mergedCells = [];
    private FrozenPane? _frozenPane;

    internal SheetBuilder(string name) => _name = name;

    // --- Columns ---

    public SheetBuilder Column(double? width = null, bool hidden = false, CellStyle? style = null)
    {
        _columns.Add(new Column(width, hidden, style));
        return this;
    }

    // --- Freeze ---

    public SheetBuilder FreezeRows(int rows)
    {
        _frozenPane = new FrozenPane(rows, _frozenPane?.Columns ?? 0);
        return this;
    }

    public SheetBuilder FreezeColumns(int columns)
    {
        _frozenPane = new FrozenPane(_frozenPane?.Rows ?? 0, columns);
        return this;
    }

    public SheetBuilder Freeze(int rows, int columns)
    {
        _frozenPane = new FrozenPane(rows, columns);
        return this;
    }

    // --- Merge ---

    public SheetBuilder Merge(string from, string to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        // Fail fast on malformed A1 references instead of emitting a corrupt merge that only
        // breaks when the workbook is opened. CellRef.ToIndex throws FormatException if invalid.
        _ = CellRef.ToIndex(from);
        _ = CellRef.ToIndex(to);
        _mergedCells.Add(new MergeRange(from, to));
        return this;
    }

    // --- Rows ---

    /// <summary>Add a header row with bold styling.</summary>
    public SheetBuilder HeaderRow(params string[] values)
    {
        var headerStyle = new CellStyle { Bold = true };
        var cells = values.Select(v => new Cell { Value = v, Style = headerStyle }).ToList();
        _rows.Add(new Row(cells));
        return this;
    }

    /// <summary>Add a row with mixed values (string, double, decimal, DateTime, bool, null).</summary>
    public SheetBuilder Row(params object?[] values)
    {
        var cells = values.Select(v => new Cell { Value = v }).ToList();
        _rows.Add(new Row(cells));
        return this;
    }

    /// <summary>Add a row of formula cells (prefix with = for formulas, otherwise treated as value).</summary>
    public SheetBuilder FormulaRow(params string?[] expressions)
    {
        var cells = expressions.Select(expr =>
        {
            if (expr is not null && expr.StartsWith('='))
                return new Cell { Formula = expr[1..] };
            return new Cell { Value = expr };
        }).ToList();
        _rows.Add(new Row(cells));
        return this;
    }

    /// <summary>Add a pre-built row.</summary>
    public SheetBuilder Row(Row row)
    {
        _rows.Add(row);
        return this;
    }

    /// <summary>Add a row with styled cells.</summary>
    public SheetBuilder StyledRow(params Cell[] cells)
    {
        // Copy: the built Row must not alias the caller's mutable array.
        _rows.Add(new Row([..cells]));
        return this;
    }

    internal Sheet Build() => new()
    {
        Name = _name,
        // Defensive copies: the returned sheet must not alias the builder's mutable backing lists.
        Columns = _columns.Count > 0 ? [.._columns] : null,
        Rows = [.._rows],
        FrozenPane = _frozenPane,
        MergedCells = _mergedCells.Count > 0 ? [.._mergedCells] : null
    };
}
