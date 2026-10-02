using Microsoft.Data.Sqlite;

namespace Pragmatic.Result.EntityFrameworkCore.Sqlite;

/// <summary>
///     SQLite exception parser using SqliteException error codes.
/// </summary>
/// <remarks>
///     <para>
///         Provides accurate error detection by reading SqliteException.SqliteErrorCode
///         and SqliteExtendedErrorCode.
///     </para>
///     <para>
///         SQLite error codes reference:
///         https://www.sqlite.org/rescode.html
///     </para>
/// </remarks>
public sealed class SqliteExceptionParser : IDbExceptionParser
{
    // SQLite primary result codes
    private const int SqliteConstraint = 19;
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;
    private const int SqliteCantopen = 14;

    // SQLite extended result codes
    private const int SqliteConstraintUnique = 2067; // SQLITE_CONSTRAINT | (11<<8)
    private const int SqliteConstraintPrimarykey = 1555; // SQLITE_CONSTRAINT | (6<<8)
    private const int SqliteConstraintForeignkey = 787; // SQLITE_CONSTRAINT | (3<<8)
    private const int SqliteConstraintNotnull = 1299; // SQLITE_CONSTRAINT | (5<<8)
    private const int SqliteConstraintCheck = 275; // SQLITE_CONSTRAINT | (1<<8)

    /// <summary>
    ///     Gets the singleton instance.
    /// </summary>
    public static SqliteExceptionParser Instance { get; } = new();

    /// <inheritdoc />
    public string ProviderName => "Sqlite";

    /// <inheritdoc />
    public bool CanParse(Exception exception)
    {
        return GetSqliteException(exception) is not null;
    }

    /// <inheritdoc />
    public DbErrorInfo? Parse(Exception exception)
    {
        var sqliteEx = GetSqliteException(exception);
        if (sqliteEx is null)
            return null;

        // First check extended error code for more specific information
        var extendedCode = sqliteEx.SqliteExtendedErrorCode;

        return extendedCode switch
        {
            SqliteConstraintUnique => CreateUniqueConstraintError(sqliteEx),
            SqliteConstraintPrimarykey => CreateUniqueConstraintError(sqliteEx),
            SqliteConstraintForeignkey => CreateForeignKeyError(sqliteEx),
            SqliteConstraintNotnull => CreateNullConstraintError(sqliteEx),
            SqliteConstraintCheck => CreateCheckConstraintError(sqliteEx),

            // Fall back to primary error code
            _ => ParsePrimaryErrorCode(sqliteEx)
        };
    }

    private static DbErrorInfo ParsePrimaryErrorCode(SqliteException sqliteEx)
    {
        return sqliteEx.SqliteErrorCode switch
        {
            SqliteConstraint => ParseConstraintMessage(sqliteEx),
            SqliteBusy => new DbErrorInfo
            {
                ErrorType = DbErrorType.Deadlock,
                ErrorCode = sqliteEx.SqliteErrorCode,
                Details = sqliteEx.Message
            },
            SqliteLocked => new DbErrorInfo
            {
                ErrorType = DbErrorType.Deadlock,
                ErrorCode = sqliteEx.SqliteErrorCode,
                Details = sqliteEx.Message
            },
            SqliteCantopen => new DbErrorInfo
            {
                ErrorType = DbErrorType.ConnectionFailure,
                ErrorCode = sqliteEx.SqliteErrorCode,
                Details = sqliteEx.Message
            },
            _ => new DbErrorInfo
            {
                ErrorType = DbErrorType.Unknown,
                ErrorCode = sqliteEx.SqliteErrorCode,
                Details = sqliteEx.Message
            }
        };
    }

    private static DbErrorInfo ParseConstraintMessage(SqliteException sqliteEx)
    {
        var message = sqliteEx.Message;

        // SQLite constraint messages are descriptive
        if (message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
            return CreateUniqueConstraintError(sqliteEx);

        if (message.Contains("NOT NULL constraint failed", StringComparison.OrdinalIgnoreCase))
            return CreateNullConstraintError(sqliteEx);

        if (message.Contains("FOREIGN KEY constraint failed", StringComparison.OrdinalIgnoreCase))
            return CreateForeignKeyError(sqliteEx);

        if (message.Contains("CHECK constraint failed", StringComparison.OrdinalIgnoreCase))
            return CreateCheckConstraintError(sqliteEx);

        // Generic constraint error
        return new DbErrorInfo
        {
            ErrorType = DbErrorType.OtherConstraint,
            ErrorCode = sqliteEx.SqliteErrorCode,
            Details = message
        };
    }

    private static SqliteException? GetSqliteException(Exception exception)
    {
        // Check if exception itself is SqliteException
        if (exception is SqliteException sqliteEx)
            return sqliteEx;

        // Check inner exception
        if (exception.InnerException is SqliteException innerSqliteEx)
            return innerSqliteEx;

        // Check deeper nesting (common with EF Core)
        if (exception.InnerException?.InnerException is SqliteException deepSqliteEx)
            return deepSqliteEx;

        return null;
    }

    private static DbErrorInfo CreateUniqueConstraintError(SqliteException sqliteEx)
    {
        // Message format: "UNIQUE constraint failed: TableName.ColumnName"
        var (tableName, columnName) = ExtractTableAndColumn(sqliteEx.Message, "UNIQUE constraint failed:");

        return new DbErrorInfo
        {
            ErrorType = DbErrorType.UniqueConstraint,
            TableName = tableName,
            ColumnName = columnName,
            ErrorCode = sqliteEx.SqliteErrorCode,
            Details = sqliteEx.Message
        };
    }

    private static DbErrorInfo CreateForeignKeyError(SqliteException sqliteEx)
    {
        return new DbErrorInfo
        {
            ErrorType = DbErrorType.ForeignKeyConstraint,
            ErrorCode = sqliteEx.SqliteErrorCode,
            Details = sqliteEx.Message
        };
    }

    private static DbErrorInfo CreateNullConstraintError(SqliteException sqliteEx)
    {
        // Message format: "NOT NULL constraint failed: TableName.ColumnName"
        var (tableName, columnName) = ExtractTableAndColumn(sqliteEx.Message, "NOT NULL constraint failed:");

        return new DbErrorInfo
        {
            ErrorType = DbErrorType.NullConstraint,
            TableName = tableName,
            ColumnName = columnName,
            ErrorCode = sqliteEx.SqliteErrorCode,
            Details = sqliteEx.Message
        };
    }

    private static DbErrorInfo CreateCheckConstraintError(SqliteException sqliteEx)
    {
        // Message format: "CHECK constraint failed: constraint_name" or "CHECK constraint failed"
        var constraintName = ExtractCheckConstraintName(sqliteEx.Message);

        return new DbErrorInfo
        {
            ErrorType = DbErrorType.CheckConstraint,
            ConstraintName = constraintName,
            ErrorCode = sqliteEx.SqliteErrorCode,
            Details = sqliteEx.Message
        };
    }

    // =========================================================================
    // Message parsing helpers
    // =========================================================================

    private static (string? TableName, string? ColumnName) ExtractTableAndColumn(string message, string prefix)
    {
        var start = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return (null, null);

        start += prefix.Length;
        var remaining = message[start..].Trim();

        // Format: "TableName.ColumnName"
        var dot = remaining.IndexOf('.');
        if (dot >= 0)
        {
            var tableName = remaining[..dot];
            var columnPart = remaining[(dot + 1)..];

            // Remove any trailing parts (like additional columns or messages)
            var end = columnPart.IndexOfAny([' ', ',', ')', '\n', '\r']);
            var columnName = end >= 0 ? columnPart[..end] : columnPart;

            return (tableName, columnName);
        }

        return (null, null);
    }

    private static string? ExtractCheckConstraintName(string message)
    {
        const string prefix = "CHECK constraint failed:";
        var start = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += prefix.Length;
        var remaining = message[start..].Trim();

        if (string.IsNullOrEmpty(remaining))
            return null;

        // Remove any trailing parts
        var end = remaining.IndexOfAny([' ', '\n', '\r']);
        return end >= 0 ? remaining[..end] : remaining;
    }
}