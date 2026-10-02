using System.Text.RegularExpressions;
using Npgsql;

namespace Pragmatic.Result.EntityFrameworkCore.PostgreSQL;

/// <summary>
///     PostgreSQL exception parser using PostgresException SqlState codes.
/// </summary>
/// <remarks>
///     <para>
///         Provides accurate error detection by reading PostgresException.SqlState
///         instead of parsing error messages.
///     </para>
///     <para>
///         PostgreSQL error codes reference:
///         https://www.postgresql.org/docs/current/errcodes-appendix.html
///     </para>
/// </remarks>
public sealed class PostgreSqlExceptionParser : IDbExceptionParser
{
    /// <summary>
    ///     Gets the singleton instance.
    /// </summary>
    public static PostgreSqlExceptionParser Instance { get; } = new();

    /// <inheritdoc />
    public string ProviderName => "PostgreSQL";

    /// <inheritdoc />
    public bool CanParse(Exception exception)
    {
        return GetPostgresException(exception) is not null;
    }

    /// <inheritdoc />
    public DbErrorInfo? Parse(Exception exception)
    {
        var pgEx = GetPostgresException(exception);
        if (pgEx is null)
            return null;

        // PostgreSQL SQLSTATE codes are 5 characters
        // Class 23 = Integrity Constraint Violation
        // Class 40 = Transaction Rollback
        // Class 08 = Connection Exception
        // Class 22 = Data Exception

        return pgEx.SqlState switch
        {
            // Unique violation (23505)
            "23505" => CreateUniqueConstraintError(pgEx),

            // Foreign key violation (23503)
            "23503" => CreateForeignKeyError(pgEx),

            // Not null violation (23502)
            "23502" => CreateNullConstraintError(pgEx),

            // Check constraint violation (23514)
            "23514" => new DbErrorInfo
            {
                ErrorType = DbErrorType.CheckConstraint,
                ConstraintName = pgEx.ConstraintName,
                TableName = pgEx.TableName,
                SqlState = pgEx.SqlState,
                Details = pgEx.Message
            },

            // String data right truncation (22001)
            "22001" => CreateMaxLengthError(pgEx),

            // Numeric value out of range (22003)
            "22003" => CreateNumericOverflowError(pgEx),

            // Deadlock detected (40P01)
            "40P01" => new DbErrorInfo
            {
                ErrorType = DbErrorType.Deadlock,
                SqlState = pgEx.SqlState,
                Details = pgEx.Message
            },

            // Statement timeout (57014 - query_canceled due to statement_timeout)
            "57014" when pgEx.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                => new DbErrorInfo
                {
                    ErrorType = DbErrorType.Timeout,
                    SqlState = pgEx.SqlState,
                    Details = pgEx.Message
                },

            // Connection exceptions (Class 08)
            _ when pgEx.SqlState?.StartsWith("08") == true
                => CreateConnectionError(pgEx),

            // Serialization failure (40001) - treat as deadlock for retry purposes
            "40001" => new DbErrorInfo
            {
                ErrorType = DbErrorType.Deadlock,
                SqlState = pgEx.SqlState,
                Details = pgEx.Message
            },

            // Unknown
            _ => new DbErrorInfo
            {
                ErrorType = DbErrorType.Unknown,
                SqlState = pgEx.SqlState,
                Details = pgEx.Message
            }
        };
    }

    private static PostgresException? GetPostgresException(Exception exception)
    {
        // Check if exception itself is PostgresException
        if (exception is PostgresException pgEx)
            return pgEx;

        // Check inner exception
        if (exception.InnerException is PostgresException innerPgEx)
            return innerPgEx;

        // Check deeper nesting (common with EF Core)
        if (exception.InnerException?.InnerException is PostgresException deepPgEx)
            return deepPgEx;

        return null;
    }

    private static DbErrorInfo CreateUniqueConstraintError(PostgresException pgEx)
    {
        return new DbErrorInfo
        {
            ErrorType = DbErrorType.UniqueConstraint,
            TableName = pgEx.TableName,
            ColumnName = pgEx.ColumnName,
            ConstraintName = pgEx.ConstraintName,
            SqlState = pgEx.SqlState,
            Details = pgEx.Message
        };
    }

    private static DbErrorInfo CreateForeignKeyError(PostgresException pgEx)
    {
        return new DbErrorInfo
        {
            ErrorType = DbErrorType.ForeignKeyConstraint,
            TableName = pgEx.TableName,
            ConstraintName = pgEx.ConstraintName,
            SqlState = pgEx.SqlState,
            Details = pgEx.Message
        };
    }

    private static DbErrorInfo CreateNullConstraintError(PostgresException pgEx)
    {
        return new DbErrorInfo
        {
            ErrorType = DbErrorType.NullConstraint,
            TableName = pgEx.TableName,
            ColumnName = pgEx.ColumnName,
            SqlState = pgEx.SqlState,
            Details = pgEx.Message
        };
    }

    private static DbErrorInfo CreateMaxLengthError(PostgresException pgEx)
    {
        // Try to extract max length from message
        int? maxLength = null;
        var match = Regex.Match(
            pgEx.Message,
            @"character varying\((\d+)\)|varchar\((\d+)\)",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var lengthStr = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (int.TryParse(lengthStr, out var len))
                maxLength = len;
        }

        return new DbErrorInfo
        {
            ErrorType = DbErrorType.MaxLengthExceeded,
            TableName = pgEx.TableName,
            ColumnName = pgEx.ColumnName,
            MaxLength = maxLength,
            SqlState = pgEx.SqlState,
            Details = pgEx.Message
        };
    }

    private static DbErrorInfo CreateNumericOverflowError(PostgresException pgEx)
    {
        return new DbErrorInfo
        {
            ErrorType = DbErrorType.NumericOverflow,
            TableName = pgEx.TableName,
            ColumnName = pgEx.ColumnName,
            SqlState = pgEx.SqlState,
            Details = pgEx.Message
        };
    }

    private static DbErrorInfo CreateConnectionError(PostgresException pgEx)
    {
        return new DbErrorInfo
        {
            ErrorType = DbErrorType.ConnectionFailure,
            SqlState = pgEx.SqlState,
            Details = pgEx.Message
        };
    }
}