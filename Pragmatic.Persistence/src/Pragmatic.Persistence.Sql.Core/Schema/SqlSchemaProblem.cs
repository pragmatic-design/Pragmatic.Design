namespace Pragmatic.Persistence.Sql.Schema;

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
