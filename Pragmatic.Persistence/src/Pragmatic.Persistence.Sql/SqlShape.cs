namespace Pragmatic.Persistence.Sql;

/// <summary>A shape the client asked for that the plan cannot give: a field outside the whitelist, an operator it does not know, a value of the wrong kind.</summary>
public sealed class SqlShapeException(string message) : Exception(message);

/// <summary>One column a shaped plan exposes to the client: its name (the row type's member) and the kind its values convert to.</summary>
public sealed record SqlShapeColumn(string Name, SqlParamKind Kind);

/// <summary>A sort the client asked for: a whitelisted field and its direction.</summary>
public sealed record SqlShapeSort(string Field, bool Descending);

/// <summary>
///     What a client (a grid) asks of a compiled chain's rows, in DevExtreme's terms: a filter over the row type's members, a sort,
///     a page (<see cref="Skip" />, <see cref="Take" />), a subset of the members (<see cref="Select" />), and the total count of the
///     filtered rows. Applied by <c>ShapeAsync</c> on top of the plan's statement (a derived table), its fields checked against the
///     plan's columns: nothing the chain did not expose can be filtered, sorted or read. <see cref="FromDevExtreme" /> reads the wire
///     format (<c>filter=[["Total",">",100],"and",["Status","=",1]]</c>, <c>sort=[{"selector":"CreatedAt","desc":true}]</c>).
/// </summary>
public sealed class SqlShape
{
    public SqlShapeFilter? Filter { get; init; }

    public IReadOnlyList<SqlShapeSort> Sort { get; init; } = [];

    public int Skip { get; init; }

    /// <summary>At most this many rows; null for every row the filter leaves (0 with <see cref="RequireTotalCount" />: the count alone).</summary>
    public int? Take { get; init; }

    /// <summary>The members to read, null for all of them: a narrowed read comes back as dictionaries, the row type is not built.</summary>
    public IReadOnlyList<string>? Select { get; init; }

    /// <summary>True to count the filtered rows too (a second statement in the same command).</summary>
    public bool RequireTotalCount { get; init; }

    /// <summary>True to compare strings case-insensitively (<c>LOWER</c> on both sides), as DevExtreme's <c>StringToLower</c> does.</summary>
    public bool CaseInsensitive { get; init; } = true;

    /// <summary>The shape from DevExtreme's load options: the filter and the sort as their JSON (null or empty for none), the page, the selected members, the count.</summary>
    public static SqlShape FromDevExtreme(string? filterJson, string? sortJson, int skip = 0, int? take = null, IReadOnlyList<string>? select = null, bool requireTotalCount = false, bool caseInsensitive = true)
        => new()
        {
            Filter = string.IsNullOrWhiteSpace(filterJson) ? null : SqlShapeFilter.Parse(System.Text.Json.JsonDocument.Parse(filterJson!).RootElement),
            Sort = string.IsNullOrWhiteSpace(sortJson) ? [] : SqlShapeFilter.ParseSort(System.Text.Json.JsonDocument.Parse(sortJson!).RootElement),
            Skip = skip,
            Take = take,
            Select = select,
            RequireTotalCount = requireTotalCount,
            CaseInsensitive = caseInsensitive,
        };
}

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
