namespace Pragmatic.Persistence.Sql.Execution;

/// <summary>
///     What an application hangs on a session's writes (<see cref="SqlSession.CommitHooks" />): a callback before the rows are written and one
///     once they are committed. A write outside a transaction is its own commit; a write inside one is reported when the transaction commits,
///     and never when it rolls back. Pragmatic's <c>ICommitObserver</c> is attached through these (<c>SqlCommitObservers</c>). Null costs nothing.
/// </summary>
public sealed class SqlCommitHooks
{
    /// <summary>Before the statements that write the rows run. Throwing refuses the write: nothing is written.</summary>
    public Action<IReadOnlyList<SqlWrittenRow>>? Writing { get; init; }

    /// <summary>After the commit that carried the rows succeeded, before its caller sees it.</summary>
    public Action<IReadOnlyList<SqlWrittenRow>>? Committed { get; init; }
}
