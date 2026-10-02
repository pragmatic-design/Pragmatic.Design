namespace Pragmatic.Result.EntityFrameworkCore;

/// <summary>
///     Represents a max length exceeded error.
/// </summary>
/// <remarks>
///     <para>
///         This error is returned when attempting to insert or update a value
///         that exceeds the maximum length defined for a column.
///     </para>
///     <para>
///         Status code 400 (Bad Request).
///     </para>
/// </remarks>
public sealed record DbMaxLengthError : Error
{
    /// <inheritdoc />
    public override string Code => "DB_MAX_LENGTH";

    /// <inheritdoc />
    public override int StatusCode => 400;

    /// <inheritdoc />
    public override string Title => "Value Too Long";

    /// <summary>
    ///     Gets the name of the table where the error occurred.
    /// </summary>
    public string? TableName { get; init; }

    /// <summary>
    ///     Gets the name of the column with the length constraint.
    /// </summary>
    public string? ColumnName { get; init; }

    /// <summary>
    ///     Gets the maximum allowed length for the column.
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    ///     Creates a max length exceeded error.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name with length constraint.</param>
    /// <param name="maxLength">The maximum allowed length.</param>
    /// <returns>A DbMaxLengthError instance.</returns>
    public static DbMaxLengthError Create(string tableName, string? columnName = null, int? maxLength = null)
    {
        return new DbMaxLengthError
        {
            TableName = tableName,
            ColumnName = columnName,
            MaxLength = maxLength
        };
    }
}