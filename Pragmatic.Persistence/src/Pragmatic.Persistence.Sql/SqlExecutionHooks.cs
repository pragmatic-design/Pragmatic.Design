using System.Data.Common;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     What an application hangs on a session's executions (<see cref="SqlSession.Hooks" />): a callback before each command runs and one
///     after it ended, with the time it took and the error when it failed. Both see the origin of the statement (a plan's call site,
///     a writer's table, a sequence's name, "write batch"). For a plan the time runs from the command's start to the last row read; for
///     a write it is the command's own. Nothing costs anything while <see cref="SqlSession.Hooks" /> is null; the metrics and traces
///     (<see cref="SqlTelemetry" />) are independent of these and keep working beside them.
/// </summary>
public sealed class SqlExecutionHooks
{
    /// <summary>Before a command runs: the session, the command with its text and its bound parameters (null for a provider batch under pipelining), the origin.</summary>
    public Action<SqlSession, DbCommand?, string>? Executing { get; init; }

    /// <summary>After a command ended: the session, the origin, the time it took, the exception when it failed (null when it did not).</summary>
    public Action<SqlSession, string, TimeSpan, Exception?>? Executed { get; init; }
}
