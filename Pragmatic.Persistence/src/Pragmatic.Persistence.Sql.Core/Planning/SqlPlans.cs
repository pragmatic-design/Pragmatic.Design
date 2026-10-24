namespace Pragmatic.Persistence.Sql.Planning;

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
