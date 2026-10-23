namespace Pragmatic.Persistence.Sql;

/// <summary>A plan seen without its row type: what a warmup needs of every plan of every translated assembly.</summary>
public interface ISqlPlan
{
    /// <summary>The slot of the plan's command in every session's cache.</summary>
    int Id { get; }

    /// <summary>True when the command can be prepared ahead: no IN list the text expands per call, no parameter of unknown kind.</summary>
    bool Preparable { get; }

    /// <summary>False where the plan has no statement for the dialect (MySQL: RETURNING/OUTPUT, a table-valued function).</summary>
    bool Supports(SqlDialect dialect);

    /// <summary>The statement for a dialect (see <see cref="SqlPlan{T}.Sql(SqlDialect)" />).</summary>
    string Sql(SqlDialect dialect);

    /// <summary>Creates and prepares the plan's command on the session when it has none yet; true when it did.</summary>
    Task<bool> WarmAsync(SqlSession session, CancellationToken ct);
}

/// <summary>
///     Every plan the generator emitted, across the assemblies loaded: each translated assembly registers its plans at module start,
///     lazily (a delegate per plan: the plan itself is built on the first use, or by <see cref="SqlSession.WarmupAsync" />).
/// </summary>
public static class SqlPlans
{
    private static readonly List<Func<ISqlPlan>> s_plans = [];

    /// <summary>Registers a plan's accessor; called by the generated module initializers.</summary>
    public static void Add(Func<ISqlPlan> plan)
    {
        lock (s_plans)
            s_plans.Add(plan);
    }

    /// <summary>The registered plans, built as they are enumerated.</summary>
    public static IReadOnlyList<Func<ISqlPlan>> All
    {
        get
        {
            lock (s_plans)
                return s_plans.ToArray();
        }
    }
}
