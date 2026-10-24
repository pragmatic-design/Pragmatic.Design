using System.Collections;
using System.Linq.Expressions;

namespace Pragmatic.Persistence.Sql.Dynamic;

/// <summary>The translation of one runtime chain: the plan (cached by shape and by text) and the values of this call.</summary>
internal readonly struct DynamicTranslation<T>
{
    public DynamicTranslation(SqlPlan<T> plan, object?[] values)
    {
        Plan = plan;
        Values = values;
    }

    public SqlPlan<T> Plan { get; }

    public object?[] Values { get; }

    public string Sql(SqlDialect dialect) => Plan.Sql(dialect);

    /// <summary>The chain the plan runs: the values of this call in slot 0.</summary>
    public (SqlPlan<T> Plan, SqlQuery<T> Query) Bind(SqlSession session) => (Plan, session.Query<T>().WithSlot(0, Values));
}
