using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pragmatic.Persistence.Sql.Execution;

/// <summary>One execution under way: its start, and its activity when traced; default when nothing listens.</summary>
internal readonly struct SqlExecution(long started, Activity? activity)
{
    public long Started { get; } = started;

    public Activity? Activity { get; } = activity;
}
