namespace Pragmatic.Persistence.Sql.Shapes;

/// <summary>What a shaped read answers: the rows as the row type (a full read), or as dictionaries of the selected members (a narrowed one), and the total when asked.</summary>
public sealed class SqlShapeResult<T>
{
    public SqlShapeResult(List<T> rows, List<Dictionary<string, object?>>? narrowed, int? totalCount, string[] columns)
    {
        Rows = rows;
        Narrowed = narrowed;
        TotalCount = totalCount;
        Columns = columns;
    }

    /// <summary>The page as the row type; empty when the shape narrowed the members (see <see cref="Narrowed" />).</summary>
    public List<T> Rows { get; }

    /// <summary>The page as one dictionary per row, the selected members as keys; null when the shape read every member.</summary>
    public List<Dictionary<string, object?>>? Narrowed { get; }

    /// <summary>The filtered rows' count, when the shape asked for it.</summary>
    public int? TotalCount { get; }

    /// <summary>The members the result carries, in order.</summary>
    public string[] Columns { get; }
}
