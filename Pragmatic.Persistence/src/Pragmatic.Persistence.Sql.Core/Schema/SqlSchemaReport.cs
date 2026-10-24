namespace Pragmatic.Persistence.Sql.Schema;

/// <summary>The outcome of a schema check: the problems found, the tables looked at.</summary>
public sealed class SqlSchemaReport
{
    internal SqlSchemaReport(IReadOnlyList<SqlSchemaProblem> problems, int tables)
    {
        Problems = problems;
        Tables = tables;
    }

    public IReadOnlyList<SqlSchemaProblem> Problems { get; }

    /// <summary>How many entity tables (and views) were checked.</summary>
    public int Tables { get; }

    /// <summary>True when the database holds every table and column the model reads, with compatible types (unmapped columns do not count).</summary>
    public bool IsCompatible => Problems.All(p => p.Kind == SqlSchemaProblemKind.UnmappedColumn);

    public override string ToString() => Problems.Count == 0 ? $"{Tables} tables as the model expects" : string.Join(Environment.NewLine, Problems.Select(p => p.ToString()));
}
