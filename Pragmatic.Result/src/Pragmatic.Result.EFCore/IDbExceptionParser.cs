namespace Pragmatic.Result.EntityFrameworkCore;

/// <summary>
///     Parses database exceptions into typed error information.
/// </summary>
/// <remarks>
///     <para>
///         Implement this interface for provider-specific exception parsing.
///         Each database provider (SQL Server, PostgreSQL, MySQL, SQLite) has
///         its own exception types with specific error codes.
///     </para>
///     <para>
///         Provider-specific implementations are available in separate packages:
///         <list type="bullet">
///             <item>Pragmatic.Result.EntityFrameworkCore.SqlServer</item>
///             <item>Pragmatic.Result.EntityFrameworkCore.PostgreSQL</item>
///             <item>Pragmatic.Result.EntityFrameworkCore.MySql</item>
///             <item>Pragmatic.Result.EntityFrameworkCore.Sqlite</item>
///         </list>
///     </para>
/// </remarks>
public interface IDbExceptionParser
{
    /// <summary>
    ///     Gets the database provider name this parser handles.
    /// </summary>
    /// <example>"SqlServer", "PostgreSQL", "MySql", "Sqlite"</example>
    string ProviderName { get; }

    /// <summary>
    ///     Determines if this parser can handle the given exception.
    /// </summary>
    /// <param name="exception">The exception to check.</param>
    /// <returns>True if this parser can handle the exception.</returns>
    bool CanParse(Exception exception);

    /// <summary>
    ///     Parses the exception into typed error information.
    /// </summary>
    /// <param name="exception">The exception to parse.</param>
    /// <returns>Parsed error information, or null if the exception type is unknown.</returns>
    DbErrorInfo? Parse(Exception exception);
}

/// <summary>
///     Represents parsed database error information.
/// </summary>
public readonly record struct DbErrorInfo
{
    /// <summary>
    ///     Gets the type of database error.
    /// </summary>
    public required DbErrorType ErrorType { get; init; }

    /// <summary>
    ///     Gets the table name involved in the error, if available.
    /// </summary>
    public string? TableName { get; init; }

    /// <summary>
    ///     Gets the column name involved in the error, if available.
    /// </summary>
    public string? ColumnName { get; init; }

    /// <summary>
    ///     Gets the constraint name involved in the error, if available.
    /// </summary>
    public string? ConstraintName { get; init; }

    /// <summary>
    ///     Gets the maximum length for max length errors.
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    ///     Gets additional details about the error.
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    ///     Gets the provider-specific error code.
    /// </summary>
    public int? ErrorCode { get; init; }

    /// <summary>
    ///     Gets the SQL state code (for providers that support it).
    /// </summary>
    public string? SqlState { get; init; }
}

/// <summary>
///     Types of database errors that can be detected.
/// </summary>
public enum DbErrorType
{
    /// <summary>
    ///     Unknown error type.
    /// </summary>
    Unknown = 0,

    /// <summary>
    ///     Unique constraint violation (duplicate key).
    /// </summary>
    UniqueConstraint = 1,

    /// <summary>
    ///     Foreign key constraint violation.
    /// </summary>
    ForeignKeyConstraint = 2,

    /// <summary>
    ///     NOT NULL constraint violation.
    /// </summary>
    NullConstraint = 3,

    /// <summary>
    ///     Value exceeds maximum length.
    /// </summary>
    MaxLengthExceeded = 4,

    /// <summary>
    ///     Numeric value overflow.
    /// </summary>
    NumericOverflow = 5,

    /// <summary>
    ///     Check constraint violation.
    /// </summary>
    CheckConstraint = 6,

    /// <summary>
    ///     Deadlock detected.
    /// </summary>
    Deadlock = 7,

    /// <summary>
    ///     Operation timed out.
    /// </summary>
    Timeout = 8,

    /// <summary>
    ///     Connection failure.
    /// </summary>
    ConnectionFailure = 9,

    /// <summary>
    ///     Other constraint violation.
    /// </summary>
    OtherConstraint = 99
}