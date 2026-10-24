namespace Pragmatic.Persistence.Sql.Filters;

/// <summary>A visibility rule: one predicate on the entity, as a lambda the generator can read.</summary>
public abstract class VisibilityRule<T>
{
    public abstract System.Linq.Expressions.Expression<Func<T, bool>> ToExpression();
}
