namespace Pragmatic.Result.EntityFrameworkCore;

/// <summary>
///     Represents a numeric overflow error.
/// </summary>
/// <remarks>
///     <para>
///         This error is returned when attempting to insert or update a numeric value
///         that exceeds the range of the target column type.
///     </para>
///     <para>
///         Status code 400 (Bad Request).
///     </para>
/// </remarks>
public sealed record DbNumericOverflowError : Error
{
    /// <inheritdoc />
    public override string Code => "DB_NUMERIC_OVERFLOW";

    /// <inheritdoc />
    public override int StatusCode => 400;

    /// <inheritdoc />
    public override string Title => "Numeric Value Out of Range";

    /// <summary>
    ///     Gets the name of the table where the error occurred.
    /// </summary>
    public string? TableName { get; init; }

    /// <summary>
    ///     Gets the name of the column with the overflow.
    /// </summary>
    public string? ColumnName { get; init; }

    /// <summary>
    ///     Gets additional details about the overflow.
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    ///     Creates a numeric overflow error.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name.</param>
    /// <param name="details">Additional details about the overflow.</param>
    /// <returns>A DbNumericOverflowError instance.</returns>
    public static DbNumericOverflowError Create(string tableName, string? columnName = null, string? details = null)
    {
        return new DbNumericOverflowError
        {
            TableName = tableName,
            ColumnName = columnName,
            Details = details
        };
    }
}