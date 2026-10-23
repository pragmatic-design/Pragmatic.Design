namespace Pragmatic.Persistence.Sql;

/// <summary>What <see cref="SqlSession.VerifySchemaAsync" /> found wrong between the generated model and the database.</summary>
public enum SqlSchemaProblemKind
{
    /// <summary>The entity's table (or view) is not in the database.</summary>
    MissingTable,

    /// <summary>A mapped column is not in the table.</summary>
    MissingColumn,

    /// <summary>The column is there with a type the model's kind does not store as.</summary>
    TypeMismatch,

    /// <summary>A column of the table no entity maps (information: the model may read a subset).</summary>
    UnmappedColumn,
}

/// <summary>One difference: the table, the column (null for a table problem), what the model expects and what the database has.</summary>
public sealed record SqlSchemaProblem(SqlSchemaProblemKind Kind, string Table, string? Column, string? Expected, string? Actual)
{
    public override string ToString() => Kind switch
    {
        SqlSchemaProblemKind.MissingTable => $"table \"{Table}\" is not in the database",
        SqlSchemaProblemKind.MissingColumn => $"\"{Table}\".\"{Column}\" is not in the table (the model expects {Expected})",
        SqlSchemaProblemKind.TypeMismatch => $"\"{Table}\".\"{Column}\" is {Actual}, the model expects {Expected}",
        _ => $"\"{Table}\".\"{Column}\" ({Actual}) is in the table and no entity maps it",
    };
}

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
