using Microsoft.Data.SqlClient;

namespace Pragmatic.Result.EntityFrameworkCore.SqlServer;

/// <summary>
///     SQL Server exception parser using SqlException error numbers.
/// </summary>
/// <remarks>
///     <para>
///         Provides accurate error detection by reading SqlException.Number
///         instead of parsing error messages.
///     </para>
///     <para>
///         SQL Server error numbers reference:
///         https://docs.microsoft.com/en-us/sql/relational-databases/errors-events/database-engine-events-and-errors
///     </para>
/// </remarks>
public sealed class SqlServerExceptionParser : IDbExceptionParser
{
    /// <summary>
    ///     Gets the singleton instance.
    /// </summary>
    public static SqlServerExceptionParser Instance { get; } = new();

    /// <inheritdoc />
    public string ProviderName => "SqlServer";

    /// <inheritdoc />
    public bool CanParse(Exception exception)
    {
        return GetSqlException(exception) is not null;
    }

    /// <inheritdoc />
    public DbErrorInfo? Parse(Exception exception)
    {
        var sqlEx = GetSqlException(exception);
        if (sqlEx is null)
            return null;

        return sqlEx.Number switch
        {
            // Unique constraint violations
            2601 => CreateUniqueConstraintError(sqlEx), // Cannot insert duplicate key row
            2627 => CreateUniqueConstraintError(sqlEx), // Violation of PRIMARY KEY/UNIQUE constraint

            // Foreign key and check constraint violations (both use error 547)
            // Check constraint must be checked first via message content
            547 when sqlEx.Message.Contains("CHECK constraint", StringComparison.OrdinalIgnoreCase)
                => new DbErrorInfo
                {
                    ErrorType = DbErrorType.CheckConstraint,
                    ErrorCode = sqlEx.Number,
                    ConstraintName = ExtractConstraintName(sqlEx.Message),
                    Details = sqlEx.Message
                },
            547 => CreateForeignKeyError(sqlEx), // INSERT/UPDATE/DELETE conflict with FOREIGN KEY constraint

            // Null constraint violations
            515 => CreateNullConstraintError(sqlEx), // Cannot insert NULL into column

            // Max length / truncation
            8152 => CreateMaxLengthError(sqlEx), // String or binary data would be truncated

            // Numeric overflow
            8115 => CreateNumericOverflowError(sqlEx), // Arithmetic overflow error

            // Deadlock
            1205 => new DbErrorInfo
            {
                ErrorType = DbErrorType.Deadlock,
                ErrorCode = sqlEx.Number,
                Details = sqlEx.Message
            },

            // Timeout
            -2 => new DbErrorInfo
            {
                ErrorType = DbErrorType.Timeout,
                ErrorCode = sqlEx.Number,
                Details = sqlEx.Message
            },
            1222 => new DbErrorInfo // Lock request timeout
            {
                ErrorType = DbErrorType.Timeout,
                ErrorCode = sqlEx.Number,
                Details = sqlEx.Message
            },

            // Connection failures
            -1 => CreateConnectionError(sqlEx), // Network/transport error
            53 => CreateConnectionError(sqlEx), // Named pipe provider error
            233 => CreateConnectionError(sqlEx), // Connection initialization error

            // Unknown - return basic info
            _ => new DbErrorInfo
            {
                ErrorType = DbErrorType.Unknown,
                ErrorCode = sqlEx.Number,
                Details = sqlEx.Message
            }
        };
    }

    private static SqlException? GetSqlException(Exception exception)
    {
        // EF Core retry pipelines and AggregateException wrapping can nest the
        // SqlException arbitrarily deep, so unwrap InnerException (and any
        // AggregateException.InnerExceptions) up to a bounded depth. The depth
        // cap doubles as cycle protection against self-referencing chains.
        const int maxDepth = 8;

        var current = exception;
        for (var depth = 0; current is not null && depth < maxDepth; depth++)
        {
            if (current is SqlException sqlEx)
                return sqlEx;

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    var found = GetSqlException(inner);
                    if (found is not null)
                        return found;
                }

                return null;
            }

            current = current.InnerException;
        }

        return null;
    }

    private static DbErrorInfo CreateUniqueConstraintError(SqlException sqlEx)
    {
        var (tableName, constraintName) = ExtractTableAndConstraint(sqlEx.Message);
        var columnName = ExtractColumnFromConstraintName(constraintName);

        return new DbErrorInfo
        {
            ErrorType = DbErrorType.UniqueConstraint,
            TableName = tableName,
            ColumnName = columnName,
            ConstraintName = constraintName,
            ErrorCode = sqlEx.Number,
            Details = sqlEx.Message
        };
    }

    private static DbErrorInfo CreateForeignKeyError(SqlException sqlEx)
    {
        var constraintName = ExtractConstraintName(sqlEx.Message);
        var tableName = ExtractTableFromFkMessage(sqlEx.Message);

        return new DbErrorInfo
        {
            ErrorType = DbErrorType.ForeignKeyConstraint,
            TableName = tableName,
            ConstraintName = constraintName,
            ErrorCode = sqlEx.Number,
            Details = sqlEx.Message
        };
    }

    private static DbErrorInfo CreateNullConstraintError(SqlException sqlEx)
    {
        var columnName = ExtractColumnName(sqlEx.Message);
        var tableName = ExtractTableName(sqlEx.Message);

        return new DbErrorInfo
        {
            ErrorType = DbErrorType.NullConstraint,
            TableName = tableName,
            ColumnName = columnName,
            ErrorCode = sqlEx.Number,
            Details = sqlEx.Message
        };
    }

    private static DbErrorInfo CreateMaxLengthError(SqlException sqlEx)
    {
        // SQL Server 2019+ includes column name in the message
        var columnName = ExtractColumnName(sqlEx.Message);

        return new DbErrorInfo
        {
            ErrorType = DbErrorType.MaxLengthExceeded,
            ColumnName = columnName,
            ErrorCode = sqlEx.Number,
            Details = sqlEx.Message
        };
    }

    private static DbErrorInfo CreateNumericOverflowError(SqlException sqlEx)
    {
        return new DbErrorInfo
        {
            ErrorType = DbErrorType.NumericOverflow,
            ErrorCode = sqlEx.Number,
            Details = sqlEx.Message
        };
    }

    private static DbErrorInfo CreateConnectionError(SqlException sqlEx)
    {
        return new DbErrorInfo
        {
            ErrorType = DbErrorType.ConnectionFailure,
            ErrorCode = sqlEx.Number,
            Details = sqlEx.Message
        };
    }

    // =========================================================================
    // Message parsing helpers
    // =========================================================================

    private static (string? TableName, string? ConstraintName) ExtractTableAndConstraint(string message)
    {
        // Pattern: "Violation of UNIQUE KEY constraint 'IX_Users_Email'. Cannot insert duplicate key in object 'dbo.Users'"
        var constraintName = ExtractConstraintName(message);
        var tableName = ExtractTableFromObjectClause(message);

        return (tableName, constraintName);
    }

    private static string? ExtractConstraintName(string message)
    {
        // Look for constraint name in single quotes
        var patterns = new[] { "constraint '", "CONSTRAINT '" };
        foreach (var pattern in patterns)
        {
            var start = message.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                start += pattern.Length;
                var end = message.IndexOf('\'', start);
                if (end > start)
                    return message[start..end];
            }
        }

        return null;
    }

    private static string? ExtractTableFromObjectClause(string message)
    {
        // Pattern: "object 'dbo.Users'" or "object 'Users'"
        const string pattern = "object '";
        var start = message.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start += pattern.Length;
            var end = message.IndexOf('\'', start);
            if (end > start)
            {
                var fullName = message[start..end];
                // Remove schema prefix if present
                var dot = fullName.LastIndexOf('.');
                return dot >= 0 ? fullName[(dot + 1)..] : fullName;
            }
        }

        return null;
    }

    private static string? ExtractTableFromFkMessage(string message)
    {
        // Pattern: "table \"dbo.Users\"" or similar
        const string pattern = "table \"";
        var start = message.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start += pattern.Length;
            var end = message.IndexOf('"', start);
            if (end > start)
            {
                var fullName = message[start..end];
                var dot = fullName.LastIndexOf('.');
                return dot >= 0 ? fullName[(dot + 1)..] : fullName;
            }
        }

        return null;
    }

    private static string? ExtractColumnName(string message)
    {
        // Pattern: "column 'Email'" or "column \"Email\""
        var patterns = new[] { ("column '", '\''), ("column \"", '"') };
        foreach (var (pattern, endChar) in patterns)
        {
            var start = message.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                start += pattern.Length;
                var end = message.IndexOf(endChar, start);
                if (end > start)
                    return message[start..end];
            }
        }

        return null;
    }

    private static string? ExtractTableName(string message)
    {
        // Pattern: "table 'Users'" or "table \"Users\""
        var patterns = new[] { ("table '", '\''), ("table \"", '"') };
        foreach (var (pattern, endChar) in patterns)
        {
            var start = message.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                start += pattern.Length;
                var end = message.IndexOf(endChar, start);
                if (end > start)
                {
                    var fullName = message[start..end];
                    var dot = fullName.LastIndexOf('.');
                    return dot >= 0 ? fullName[(dot + 1)..] : fullName;
                }
            }
        }

        return null;
    }

    private static string? ExtractColumnFromConstraintName(string? constraintName)
    {
        // Try to extract column name from constraint name like "IX_Users_Email" -> "Email"
        if (constraintName is null)
            return null;

        var parts = constraintName.Split('_');
        return parts.Length >= 3 ? parts[^1] : null;
    }
}