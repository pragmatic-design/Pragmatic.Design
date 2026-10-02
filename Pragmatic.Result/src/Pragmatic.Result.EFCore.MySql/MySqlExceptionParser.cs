using System.Data.Common;
using MySqlConnector;

namespace Pragmatic.Result.EntityFrameworkCore.MySql;

/// <summary>
///     MySQL exception parser using the server's error numbers.
/// </summary>
/// <remarks>
///     <para>
///         Provides accurate error detection by reading the error number instead of parsing error
///         messages.
///     </para>
///     <para>
///         Both MySQL drivers are read. MySqlConnector's <c>MySqlException</c> by its type. Oracle's
///         <c>MySql.Data</c> — the driver under <c>MySql.EntityFrameworkCore</c>, the EF Core 10 provider a
///         <c>DatabaseProvider.MySql</c> host runs on — through <see cref="Exception.Data" />: every one of
///         its constructors that takes a server error number writes it there under
///         <c>"Server Error Code"</c>. That keeps <c>MySql.Data</c>, which is GPL-2.0, out of this
///         package's references.
///     </para>
///     <para>
///         MySQL error codes reference:
///         https://dev.mysql.com/doc/mysql-errors/8.0/en/server-error-reference.html
///     </para>
/// </remarks>
public sealed class MySqlExceptionParser : IDbExceptionParser
{
    /// <summary>
    ///     Gets the singleton instance.
    /// </summary>
    public static MySqlExceptionParser Instance { get; } = new();

    /// <inheritdoc />
    public string ProviderName => "MySql";

    /// <inheritdoc />
    public bool CanParse(Exception exception)
    {
        return GetMySqlError(exception) is not null;
    }

    /// <inheritdoc />
    public DbErrorInfo? Parse(Exception exception)
    {
        if (GetMySqlError(exception) is not { } mysqlEx)
            return null;

        return mysqlEx.Number switch
        {
            // Duplicate entry (1062)
            1062 => CreateUniqueConstraintError(mysqlEx),

            // Foreign key constraint fails (1451, 1452)
            1451 => CreateForeignKeyError(mysqlEx), // Cannot delete or update parent row
            1452 => CreateForeignKeyError(mysqlEx), // Cannot add or update child row

            // Column cannot be null (1048)
            1048 => CreateNullConstraintError(mysqlEx),

            // Data too long for column (1406)
            1406 => CreateMaxLengthError(mysqlEx),

            // Out of range value (1264)
            1264 => CreateNumericOverflowError(mysqlEx),

            // Deadlock (1213)
            1213 => new DbErrorInfo
            {
                ErrorType = DbErrorType.Deadlock,
                ErrorCode = mysqlEx.Number,
                Details = mysqlEx.Message
            },

            // Lock wait timeout (1205)
            1205 => new DbErrorInfo
            {
                ErrorType = DbErrorType.Timeout,
                ErrorCode = mysqlEx.Number,
                Details = mysqlEx.Message
            },

            // Check constraint violation (3819 in MySQL 8.0.16+)
            3819 => new DbErrorInfo
            {
                ErrorType = DbErrorType.CheckConstraint,
                ConstraintName = ExtractConstraintName(mysqlEx.Message),
                ErrorCode = mysqlEx.Number,
                Details = mysqlEx.Message
            },

            // Connection errors
            0 => CreateConnectionError(mysqlEx), // Unable to connect
            1042 => CreateConnectionError(mysqlEx), // Unable to connect to any host
            1043 => CreateConnectionError(mysqlEx), // Bad handshake
            1044 => CreateConnectionError(mysqlEx), // Access denied
            1045 => CreateConnectionError(mysqlEx), // Access denied for user
            2002 => CreateConnectionError(mysqlEx), // Can't connect through socket
            2003 => CreateConnectionError(mysqlEx), // Can't connect to server
            2006 => CreateConnectionError(mysqlEx), // Server has gone away
            2013 => CreateConnectionError(mysqlEx), // Lost connection during query

            // Unknown
            _ => new DbErrorInfo
            {
                ErrorType = DbErrorType.Unknown,
                ErrorCode = mysqlEx.Number,
                Details = mysqlEx.Message
            }
        };
    }

    /// <summary>The key under which MySql.Data records the server's error number.</summary>
    private const string MySqlDataErrorNumberKey = "Server Error Code";

    /// <summary>
    ///     The server error in the exception, its inner one, or the one inside that (EF Core wraps it),
    ///     from whichever driver raised it.
    /// </summary>
    private static (int Number, string Message)? GetMySqlError(Exception exception)
        => ErrorOf(exception) ?? ErrorOf(exception.InnerException) ?? ErrorOf(exception.InnerException?.InnerException);

    private static (int Number, string Message)? ErrorOf(Exception? exception) => exception switch
    {
        MySqlException connector => (connector.Number, connector.Message),
        DbException oracle when oracle.Data[MySqlDataErrorNumberKey] is int number => (number, oracle.Message),
        _ => null
    };

    private static DbErrorInfo CreateUniqueConstraintError((int Number, string Message) mysqlEx)
    {
        // Message format: "Duplicate entry 'value' for key 'constraint_name'"
        var constraintName = ExtractKeyName(mysqlEx.Message);
        var columnName = ExtractColumnFromKeyName(constraintName);

        return new DbErrorInfo
        {
            ErrorType = DbErrorType.UniqueConstraint,
            ColumnName = columnName,
            ConstraintName = constraintName,
            ErrorCode = mysqlEx.Number,
            Details = mysqlEx.Message
        };
    }

    private static DbErrorInfo CreateForeignKeyError((int Number, string Message) mysqlEx)
    {
        var constraintName = ExtractConstraintName(mysqlEx.Message);
        var tableName = ExtractTableName(mysqlEx.Message);

        return new DbErrorInfo
        {
            ErrorType = DbErrorType.ForeignKeyConstraint,
            TableName = tableName,
            ConstraintName = constraintName,
            ErrorCode = mysqlEx.Number,
            Details = mysqlEx.Message
        };
    }

    private static DbErrorInfo CreateNullConstraintError((int Number, string Message) mysqlEx)
    {
        var columnName = ExtractColumnName(mysqlEx.Message);

        return new DbErrorInfo
        {
            ErrorType = DbErrorType.NullConstraint,
            ColumnName = columnName,
            ErrorCode = mysqlEx.Number,
            Details = mysqlEx.Message
        };
    }

    private static DbErrorInfo CreateMaxLengthError((int Number, string Message) mysqlEx)
    {
        var columnName = ExtractColumnName(mysqlEx.Message);

        return new DbErrorInfo
        {
            ErrorType = DbErrorType.MaxLengthExceeded,
            ColumnName = columnName,
            ErrorCode = mysqlEx.Number,
            Details = mysqlEx.Message
        };
    }

    private static DbErrorInfo CreateNumericOverflowError((int Number, string Message) mysqlEx)
    {
        var columnName = ExtractColumnName(mysqlEx.Message);

        return new DbErrorInfo
        {
            ErrorType = DbErrorType.NumericOverflow,
            ColumnName = columnName,
            ErrorCode = mysqlEx.Number,
            Details = mysqlEx.Message
        };
    }

    private static DbErrorInfo CreateConnectionError((int Number, string Message) mysqlEx)
    {
        return new DbErrorInfo
        {
            ErrorType = DbErrorType.ConnectionFailure,
            ErrorCode = mysqlEx.Number,
            Details = mysqlEx.Message
        };
    }

    // =========================================================================
    // Message parsing helpers
    // =========================================================================

    private static string? ExtractKeyName(string message)
    {
        // Pattern: "for key 'key_name'"
        const string pattern = "for key '";
        var start = message.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start += pattern.Length;
            var end = message.IndexOf('\'', start);
            if (end > start)
                return message[start..end];
        }

        return null;
    }

    private static string? ExtractConstraintName(string message)
    {
        // Pattern: "CONSTRAINT `constraint_name`" or "constraint `constraint_name`"
        var patterns = new[] { "CONSTRAINT `", "constraint `" };
        foreach (var pattern in patterns)
        {
            var start = message.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                start += pattern.Length;
                var end = message.IndexOf('`', start);
                if (end > start)
                    return message[start..end];
            }
        }

        return null;
    }

    private static string? ExtractTableName(string message)
    {
        // Pattern: "table `table_name`"
        const string pattern = "table `";
        var start = message.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start += pattern.Length;
            var end = message.IndexOf('`', start);
            if (end > start)
                return message[start..end];
        }

        return null;
    }

    private static string? ExtractColumnName(string message)
    {
        // Pattern: "Column 'column_name'" or "column 'column_name'"
        var patterns = new[] { "Column '", "column '" };
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

    private static string? ExtractColumnFromKeyName(string? keyName)
    {
        // Try to extract column name from key name like "IX_Users_Email" -> "Email"
        if (keyName is null)
            return null;

        var parts = keyName.Split('_');
        return parts.Length >= 3 ? parts[^1] : null;
    }
}