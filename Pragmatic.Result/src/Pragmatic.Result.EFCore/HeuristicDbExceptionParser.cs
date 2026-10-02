using System.Text.RegularExpressions;

namespace Pragmatic.Result.EntityFrameworkCore;

/// <summary>
///     Best-effort fallback exception parser using message-based heuristics.
/// </summary>
/// <remarks>
///     <para>
///         This parser matches patterns in the exception <em>message text</em> to guess the error
///         type. It is intentionally the last resort in <see cref="DbExceptionParserRegistry" />:
///         it is inherently fragile because it depends on English, provider-specific phrasing that
///         changes between versions and does not survive localized (non-English) server messages.
///     </para>
///     <para>
///         For reliable detection, register a provider-specific package. Those parsers read the
///         native, stable error <em>codes</em> (e.g. <c>SqlException.Number</c>,
///         <c>PostgresException.SqlState</c>) rather than the message text and are always tried
///         before this fallback:
///         <list type="bullet">
///             <item>Pragmatic.Result.EntityFrameworkCore.SqlServer</item>
///             <item>Pragmatic.Result.EntityFrameworkCore.PostgreSQL</item>
///             <item>Pragmatic.Result.EntityFrameworkCore.MySql</item>
///             <item>Pragmatic.Result.EntityFrameworkCore.Sqlite</item>
///         </list>
///     </para>
///     <para>
///         Specific constraint violations are checked before the broad transient heuristics so a real
///         constraint failure whose message happens to contain a transient keyword is not misread as
///         retryable.
///     </para>
/// </remarks>
public sealed class HeuristicDbExceptionParser : IDbExceptionParser
{
    /// <summary>
    ///     Gets the singleton instance.
    /// </summary>
    public static HeuristicDbExceptionParser Instance { get; } = new();

    /// <inheritdoc />
    public string ProviderName => "Heuristic";

    /// <inheritdoc />
    public bool CanParse(Exception exception)
    {
        return true;
        // Fallback, always tries
    }

    /// <inheritdoc />
    public DbErrorInfo? Parse(Exception exception)
    {
        var message = GetErrorMessage(exception);

        // Specific constraint violations first: their messages carry unambiguous phrases/codes, and a
        // genuine constraint failure must not be misclassified as a (retryable) transient error just
        // because its text mentions a transient-sounding word.
        if (IsUniqueConstraint(message))
            return new DbErrorInfo
            {
                ErrorType = DbErrorType.UniqueConstraint,
                ColumnName = ExtractUniqueConstraintColumn(message),
                ConstraintName = ExtractConstraintName(message),
                Details = message
            };

        if (IsNullConstraint(message))
            return new DbErrorInfo
            {
                ErrorType = DbErrorType.NullConstraint,
                ColumnName = ExtractNullConstraintColumn(message),
                Details = message
            };

        if (IsForeignKeyConstraint(message))
            return new DbErrorInfo
            {
                ErrorType = DbErrorType.ForeignKeyConstraint,
                ConstraintName = ExtractConstraintName(message),
                Details = message
            };

        if (IsMaxLengthExceeded(message))
        {
            var (columnName, maxLength) = ExtractMaxLengthInfo(message);
            return new DbErrorInfo
            {
                ErrorType = DbErrorType.MaxLengthExceeded,
                ColumnName = columnName,
                MaxLength = maxLength,
                Details = message
            };
        }

        if (IsNumericOverflow(message))
            return new DbErrorInfo
            {
                ErrorType = DbErrorType.NumericOverflow,
                ColumnName = ExtractColumnName(message),
                Details = message
            };

        // Transient categories last: their heuristics match broad words ("timeout", "deadlock").
        if (IsDeadlock(message))
            return new DbErrorInfo { ErrorType = DbErrorType.Deadlock, Details = message };

        if (IsTimeout(message))
            return new DbErrorInfo { ErrorType = DbErrorType.Timeout, Details = message };

        if (IsConnectionFailure(message))
            return new DbErrorInfo { ErrorType = DbErrorType.ConnectionFailure, Details = message };

        // Unknown error
        return new DbErrorInfo { ErrorType = DbErrorType.Unknown, Details = message };
    }

    private static string GetErrorMessage(Exception exception)
    {
        // Prefer inner exception message as it usually has more details
        return exception.InnerException?.Message ?? exception.Message;
    }

    // =========================================================================
    // Detection heuristics
    // =========================================================================

    // Matches a numeric error code as a whole token so that arbitrary digit runs
    // inside table names or text (e.g. "column 23505 exceeded") cannot trigger a
    // false constraint classification. A code matches only when it is not flanked
    // by other digits — its neighbours must be a non-digit or a string boundary.
    private static bool ContainsCode(string message, string code)
    {
        var index = 0;
        while ((index = message.IndexOf(code, index, StringComparison.Ordinal)) >= 0)
        {
            var before = index == 0 || !char.IsDigit(message[index - 1]);
            var afterIndex = index + code.Length;
            var after = afterIndex >= message.Length || !char.IsDigit(message[afterIndex]);
            if (before && after)
                return true;

            index = afterIndex;
        }

        return false;
    }

    private static bool IsUniqueConstraint(string message)
    {
        return message.Contains("unique", StringComparison.OrdinalIgnoreCase)
               || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
               || message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase)
               || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
               || message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
               || message.Contains("violation of UNIQUE KEY", StringComparison.OrdinalIgnoreCase)
               // SQL Server error numbers (whole-token match, see ContainsCode)
               || ContainsCode(message, "2601") || ContainsCode(message, "2627")
               // PostgreSQL error code
               || ContainsCode(message, "23505");
    }

    private static bool IsForeignKeyConstraint(string message)
    {
        return message.Contains("foreign key", StringComparison.OrdinalIgnoreCase)
               || message.Contains("FOREIGN KEY constraint", StringComparison.OrdinalIgnoreCase)
               || message.Contains("REFERENCE constraint", StringComparison.OrdinalIgnoreCase)
               // SQL Server error number (whole-token match, see ContainsCode)
               || ContainsCode(message, "547")
               // PostgreSQL error code
               || ContainsCode(message, "23503");
    }

    private static bool IsNullConstraint(string message)
    {
        return message.Contains("cannot be null", StringComparison.OrdinalIgnoreCase)
               || message.Contains("not-null constraint", StringComparison.OrdinalIgnoreCase)
               || message.Contains("NOT NULL constraint failed", StringComparison.OrdinalIgnoreCase)
               || message.Contains("cannot insert the value null", StringComparison.OrdinalIgnoreCase)
               // PostgreSQL error code (whole-token match, see ContainsCode)
               || ContainsCode(message, "23502");
    }

    private static bool IsMaxLengthExceeded(string message)
    {
        return message.Contains("would be truncated", StringComparison.OrdinalIgnoreCase)
               || message.Contains("value too long", StringComparison.OrdinalIgnoreCase)
               || message.Contains("data too long", StringComparison.OrdinalIgnoreCase)
               || message.Contains("string or binary data", StringComparison.OrdinalIgnoreCase)
               // SQL Server error number (whole-token match, see ContainsCode)
               || ContainsCode(message, "8152");
    }

    private static bool IsNumericOverflow(string message)
    {
        return message.Contains("overflow", StringComparison.OrdinalIgnoreCase)
               || message.Contains("out of range", StringComparison.OrdinalIgnoreCase)
               // SQL Server error number (whole-token match, see ContainsCode)
               || ContainsCode(message, "8115")
               // PostgreSQL error code
               || ContainsCode(message, "22003");
    }

    private static bool IsDeadlock(string message)
    {
        return message.Contains("deadlock", StringComparison.OrdinalIgnoreCase)
               // SQL Server error number (whole-token match, see ContainsCode)
               || ContainsCode(message, "1205");
    }

    private static bool IsTimeout(string message)
    {
        return message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
               || message.Contains("wait time exceeded", StringComparison.OrdinalIgnoreCase)
               // SQL Server error number — lock request timeout (whole-token match, see ContainsCode)
               || ContainsCode(message, "1222");
    }

    private static bool IsConnectionFailure(string message)
    {
        // Mirrors the DetailedDetection sibling: require "connection"
        // paired with a real connection fault, OR an explicit transport/host phrase.
        // The lone "closed"/"reset" terms are not used — even guarded by "connection"
        // they are over-broad (e.g. "connection result set closed",
        // "connection counter was reset") and would misclassify.
        var hasConnectionFault =
            message.Contains("connection", StringComparison.OrdinalIgnoreCase)
            && (message.Contains("failed", StringComparison.OrdinalIgnoreCase)
                || message.Contains("refused", StringComparison.OrdinalIgnoreCase)
                || message.Contains("broken", StringComparison.OrdinalIgnoreCase));

        return hasConnectionFault
               || message.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
               || message.Contains("connection closed", StringComparison.OrdinalIgnoreCase)
               || message.Contains("transport-level error", StringComparison.OrdinalIgnoreCase)
               || message.Contains("server was not found", StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // Extraction helpers
    // =========================================================================

    private static string? ExtractUniqueConstraintColumn(string message)
    {
        // SQLite: "UNIQUE constraint failed: Table.Column"
        if (message.Contains("UNIQUE constraint failed:"))
        {
            var start = message.IndexOf("UNIQUE constraint failed:", StringComparison.OrdinalIgnoreCase) + 25;
            var remaining = message[start..].Trim();
            var dot = remaining.IndexOf('.');
            if (dot >= 0)
            {
                var columnPart = remaining[(dot + 1)..];
                var end = columnPart.IndexOfAny([' ', ',', ')']);
                return end >= 0 ? columnPart[..end] : columnPart;
            }
        }

        return null;
    }

    private static string? ExtractNullConstraintColumn(string message)
    {
        // SQLite: "NOT NULL constraint failed: Table.Column"
        if (message.Contains("NOT NULL constraint failed:"))
        {
            var start = message.IndexOf("NOT NULL constraint failed:", StringComparison.OrdinalIgnoreCase) + 27;
            var remaining = message[start..].Trim();
            var dot = remaining.IndexOf('.');
            if (dot >= 0)
            {
                var columnPart = remaining[(dot + 1)..];
                var end = columnPart.IndexOfAny([' ', ',', ')']);
                return end >= 0 ? columnPart[..end] : columnPart;
            }
        }

        // SQL Server/MySQL: "column 'X'"
        return ExtractQuotedValue(message, "column '")
               ?? ExtractQuotedValue(message, "column \"", '"');
    }

    private static string? ExtractColumnName(string message)
    {
        return ExtractQuotedValue(message, "column '")
               ?? ExtractQuotedValue(message, "column \"", '"');
    }

    // Delegated to SqlMessageParsing: the previous inline patterns ("\"FK_" etc.) skipped PAST the
    // FK_/IX_ prefix, so the reported constraint name lost it ("Orders_Users" instead of
    // "FK_Orders_Users").
    private static string? ExtractConstraintName(string message)
        => SqlMessageParsing.ExtractConstraintName(message);

    private static (string? ColumnName, int? MaxLength) ExtractMaxLengthInfo(string message)
    {
        var columnName = ExtractColumnName(message);

        // Try to extract max length from "character varying(50)" or "varchar(50)"
        int? maxLength = null;
        var typeMatch = Regex.Match(
            message,
            @"(?:varchar|character varying|nvarchar|char)\s*\(\s*(\d+)\s*\)",
            RegexOptions.IgnoreCase);
        if (typeMatch.Success && int.TryParse(typeMatch.Groups[1].Value, out var len))
            maxLength = len;

        return (columnName, maxLength);
    }

    private static string? ExtractQuotedValue(string message, string prefix, char endQuote = '\'')
        => SqlMessageParsing.ExtractQuotedValue(message, prefix, endQuote);
}