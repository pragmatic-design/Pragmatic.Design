using Pragmatic.Specification;
using Pragmatic.Specification.Specifications;

namespace Pragmatic.Persistence.Documents.Pragmatic;

/// <summary>
///     A specification as the documents engine runs it: its composition (<c>&amp;</c>, <c>|</c>, <c>!</c>, <c>Spec&lt;T&gt;.True</c>) walked down to
///     its compiled leaves, as a <see cref="DocPredicate{T}" /> a chain reads with <c>Matching</c>.
/// </summary>
public static class DocSpecifications
{
    /// <summary>The predicate of <paramref name="specification" />; a leaf the engine did not compile is refused: its documents would otherwise be read unfiltered.</summary>
    public static DocPredicate<T> ToPredicate<T>(ISpecification<T> specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return specification switch
        {
            TrueSpecification<T> => DocPredicate<T>.True,
            AndSpecification<T> and => DocPredicate<T>.And(ToPredicate(and.Left), ToPredicate(and.Right)),
            OrSpecification<T> or => DocPredicate<T>.Or(ToPredicate(or.Left), ToPredicate(or.Right)),
            NotSpecification<T> not => DocPredicate<T>.Not(ToPredicate(not.Inner)),
            DocCompiledSpecification<T> leaf => DocPredicate<T>.Leaf(leaf.Predicate, leaf.Values),
            Specification<T> other => throw NotCompiled(specification, $"its leaf {other.GetType().Name} was not compiled by the documents engine"),
            _ => throw NotCompiled(specification, "it is not a Specification<T>"),
        };
    }

    private static NotSupportedException NotCompiled<T>(ISpecification<T> specification, string why)
        => new($"A specification of {typeof(T).Name} cannot run on the documents engine: {why}. Write it with Spec<{typeof(T).Name}>.Where(...) in a project the documents generator compiles; its condition is translated there.");
}
