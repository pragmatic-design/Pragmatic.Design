namespace Pragmatic.Result.EntityFrameworkCore;

/// <summary>
///     Represents a NOT NULL constraint violation error.
/// </summary>
/// <remarks>
///     <para>
///         This error is returned when attempting to insert or update a row
///         with a NULL value in a column that doesn't allow NULL.
///     </para>
///     <para>
///         Status code 400 (Bad Request).
///     </para>
/// </remarks>
public sealed record DbNullConstraintError : Error
{
    /// <inheritdoc />
    public override string Code => "DB_NULL_CONSTRAINT";

    /// <inheritdoc />
    public override int StatusCode => 400;

    /// <inheritdoc />
    public override string Title => "Required Value Missing";

    /// <summary>
    ///     Gets the name of the table where the constraint was violated.
    /// </summary>
    public string? TableName { get; init; }

    /// <summary>
    ///     Gets the name of the column that cannot be null.
    /// </summary>
    public string? ColumnName { get; init; }

    /// <summary>
    ///     Creates a NOT NULL constraint violation error.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name that cannot be null.</param>
    /// <returns>A DbNullConstraintError instance.</returns>
    public static DbNullConstraintError Create(string tableName, string? columnName = null)
    {
        return new DbNullConstraintError
        {
            TableName = tableName,
            ColumnName = columnName
        };
    }
}