using Pragmatic.Specification;
using Pragmatic.Specification.Specifications;

namespace Pragmatic.Persistence.Sql.Pragmatic;

/// <summary>
///     A specification as the Sql engine runs it: its composition (<c>&amp;</c>, <c>|</c>, <c>!</c>, <c>Spec&lt;T&gt;.True</c>) walked down to its
///     compiled leaves, as a <see cref="SqlPredicate{T}" /> a chain reads with <c>Matching</c>.
/// </summary>
public static class SqlSpecifications
{
    /// <summary>
    ///     The predicate of <paramref name="specification" />; a leaf the engine did not compile (a <c>Spec&lt;T&gt;.Where</c> from an assembly built
    ///     without the Sql generator, a hand-written subclass) is refused: its rows would otherwise be read unfiltered.
    /// </summary>
    public static SqlPredicate<T> ToPredicate<T>(ISpecification<T> specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return specification switch
        {
            TrueSpecification<T> => SqlPredicate<T>.True,
            AndSpecification<T> and => SqlPredicate<T>.And(ToPredicate(and.Left), ToPredicate(and.Right)),
            OrSpecification<T> or => SqlPredicate<T>.Or(ToPredicate(or.Left), ToPredicate(or.Right)),
            NotSpecification<T> not => SqlPredicate<T>.Not(ToPredicate(not.Inner)),
            CompiledSpecification<T> leaf => SqlPredicate<T>.Leaf(leaf.Predicate, leaf.Values),
            Specification<T> other => throw NotCompiled(specification, $"its leaf {other.GetType().Name} was not compiled by the Sql engine"),
            _ => throw NotCompiled(specification, "it is not a Specification<T>"),
        };
    }

    private static NotSupportedException NotCompiled<T>(ISpecification<T> specification, string why)
        => new($"A specification of {typeof(T).Name} cannot run on the Sql engine: {why}. Write it with Spec<{typeof(T).Name}>.Where(...) in a project the Sql generator compiles; its predicate is translated there.");
}
