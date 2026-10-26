using System.Data;
using System.Data.Common;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     How <see cref="SqlSession.TransactAsync" /> runs a unit of work: at which isolation level, how many attempts when the database
///     reports a transient failure (a serialization failure or a deadlock: the transaction is rolled back and the work runs again
///     from the start), and how long to wait between them (doubling each time).
/// </summary>
public sealed class SqlTransactionOptions
{
    public static readonly SqlTransactionOptions Default = new();

    public IsolationLevel Isolation { get; init; } = IsolationLevel.Unspecified;

    /// <summary>How many times the work runs at most; 1 means no retry.</summary>
    public int Attempts { get; init; } = 3;

    public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    ///     What counts as transient: by default the provider's own word (<see cref="DbException.IsTransient" />) and SQLSTATE 40001
    ///     (serialization failure) and 40P01 (deadlock), which PostgreSQL raises under SERIALIZABLE and REPEATABLE READ; SQL Server's
    ///     deadlock (1205) and SQLite's busy (5) come through the provider's flag where it sets one, or through this predicate. Oracle's
    ///     serialization failure, ORA-08177, carries no SQLSTATE and no flag: it is read off the message, as the rule violations are
    ///     (<c>SqlRuleViolations</c>).
    /// </summary>
    public Func<DbException, bool> IsTransient { get; init; } = static e => e.IsTransient || e.SqlState is "40001" or "40P01"
        || e.Message.Contains("ORA-08177", StringComparison.Ordinal);
}
