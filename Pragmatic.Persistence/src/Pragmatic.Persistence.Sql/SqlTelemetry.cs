using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     What a plan's execution reports, when someone listens: a count and a duration per plan through <see cref="System.Diagnostics.Metrics" />
///     (meter <c>Pragmatic.Persistence.Sql</c>: <c>pragmatic.sql.executions</c>, <c>pragmatic.sql.duration</c> in ms, tagged with the plan's
///     origin and the database), and an <see cref="Activity" /> per execution (source <c>Pragmatic.Persistence.Sql</c>, the same tags) for
///     tracing. Nothing is measured or allocated while no listener is attached.
/// </summary>
public static class SqlTelemetry
{
    public const string Name = "Pragmatic.Persistence.Sql";

    public static readonly Meter Meter = new(Name);

    public static readonly Counter<long> Executions = Meter.CreateCounter<long>("pragmatic.sql.executions", "{execution}", "Plan executions, by plan origin and database.");

    public static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("pragmatic.sql.duration", "ms", "Time from the command's start to the last row read, by plan origin and database.");

    public static readonly ActivitySource Source = new(Name);

    private static readonly string[] Systems = ["sqlite", "postgresql", "mssql", "mysql", "oracle"];

    /// <summary>
    ///     The start of a plan's execution: the session's hooks told (<see cref="SqlExecutionHooks.Executing" />), its timestamp and activity when
    ///     something listens or a hook wants the time, nothing otherwise.
    /// </summary>
    internal static SqlExecution Begin(SqlSession session, string origin, DbCommand command)
    {
        var hooks = session.Hooks;
        hooks?.Executing?.Invoke(session, command, origin);
        var measured = Executions.Enabled || Duration.Enabled || hooks?.Executed is not null;
        var activity = Source.HasListeners() ? Source.StartActivity("sql", ActivityKind.Client) : null;
        if (!measured && activity is null)
            return default;
        activity?.SetTag("pragmatic.plan", origin);
        activity?.SetTag("db.system", Systems[(int)session.Dialect]);
        return new SqlExecution(Stopwatch.GetTimestamp(), activity);
    }

    /// <summary>The end of an execution begun with <see cref="Begin" />: the count and the duration recorded, the activity stopped (an error on it), the session's hooks told.</summary>
    internal static void End(in SqlExecution execution, SqlSession session, string origin, Exception? error)
    {
        if (execution.Started == 0)
            return;
        var elapsed = Stopwatch.GetElapsedTime(execution.Started);
        if (Executions.Enabled || Duration.Enabled)
        {
            var tags = new TagList { { "pragmatic.plan", origin }, { "db.system", Systems[(int)session.Dialect] } };
            Executions.Add(1, tags);
            Duration.Record(elapsed.TotalMilliseconds, tags);
        }

        if (error is not null)
            execution.Activity?.SetStatus(ActivityStatusCode.Error, error.Message);
        execution.Activity?.Dispose();
        session.Hooks?.Executed?.Invoke(session, origin, elapsed, error);
    }
}

/// <summary>One execution under way: its start, and its activity when traced; default when nothing listens.</summary>
internal readonly struct SqlExecution(long started, Activity? activity)
{
    public long Started { get; } = started;

    public Activity? Activity { get; } = activity;
}
