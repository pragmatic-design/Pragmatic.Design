using System.Linq.Expressions;
using Pragmatic.Persistence.Sql;
using Pragmatic.Specification;

namespace Pragmatic.Persistence.Sql.Pragmatic;

/// <summary>
///     A <c>Spec&lt;T&gt;.Where(predicate)</c> the Sql engine compiled: the generator intercepts the call and returns this instead of
///     Pragmatic's own leaf. It is the same specification to every other reader (<see cref="ToExpression" /> is the lambda written, which
///     EF Core and the in-memory checks read), and it carries what the Sql engine runs: the compiled predicate and the values read from
///     the lambda when the specification was built.
/// </summary>
public sealed class CompiledSpecification<T>(SqlCompiledPredicate predicate, object?[] values, Expression<Func<T, bool>> expression) : Specification<T>
{
    public SqlCompiledPredicate Predicate { get; } = predicate;

    /// <summary>The values the predicate reads, in its token order, read off the lambda's tree.</summary>
    public object?[] Values { get; } = values;

    public override Expression<Func<T, bool>> ToExpression() => expression;
}
