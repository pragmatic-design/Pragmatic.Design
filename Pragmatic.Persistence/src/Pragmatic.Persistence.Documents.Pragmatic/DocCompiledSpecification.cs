using System.Linq.Expressions;
using Pragmatic.Specification;

namespace Pragmatic.Persistence.Documents.Pragmatic;

/// <summary>
///     A <c>Spec&lt;T&gt;.Where(predicate)</c> the documents engine compiled: the generator intercepts the call and returns this instead of
///     Pragmatic's own leaf. To every other reader it is the same specification (<see cref="ToExpression" /> is the lambda written); it carries
///     the compiled condition and the values read off the lambda when it was built (the Sql engine's <c>CompiledSpecification</c>, for documents).
/// </summary>
public sealed class DocCompiledSpecification<T>(DocCompiledPredicate predicate, object?[] values, Expression<Func<T, bool>> expression) : Specification<T>
{
    public DocCompiledPredicate Predicate { get; } = predicate;

    /// <summary>The values the condition reads, in its order, read off the lambda's tree.</summary>
    public object?[] Values { get; } = values;

    public override Expression<Func<T, bool>> ToExpression() => expression;
}
