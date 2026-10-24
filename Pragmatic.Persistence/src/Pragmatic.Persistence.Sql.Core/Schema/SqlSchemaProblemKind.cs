namespace Pragmatic.Persistence.Sql.Schema;

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
