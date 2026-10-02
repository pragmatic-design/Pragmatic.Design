namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     One settable property of a joined query's result, and where its value comes from.
/// </summary>
/// <remarks>
///     <para>
///         A <c>Projection</c> is one entity in and one result out, so it cannot carry a second source.
///         A query with a key join therefore builds the whole step itself and has to know, per result
///         property, whether the value is read from the root or from one of the joined targets — which
///         is what this record says.
///     </para>
///     <para>
///         ⚠️ Resolution order matters and is deliberately root-first: an <c>OrderRow.Reference</c> is
///         the order's, even if the customer happens to have one too. A name that neither side answers
///         is <c>PRAG0740</c>, reported at the declaration rather than left to become a <c>CS0117</c>
///         inside a generated file.
///     </para>
/// </remarks>
internal sealed record JoinedResultPropertyModel
{
    /// <summary>The property on the result type.</summary>
    public required string Name { get; init; }

    /// <summary>The property to read on the source.</summary>
    public required string SourceProperty { get; init; }

    /// <summary>
    ///     The index into the query's key joins, or <c>-1</c> when the value comes from the root entity.
    /// </summary>
    public required int JoinIndex { get; init; }

    /// <summary>Whether the value comes from the root entity.</summary>
    public bool IsFromRoot => JoinIndex < 0;

    /// <summary>
    ///     Whether the read has to survive a missing target.
    /// </summary>
    /// <remarks>
    ///     True exactly for a property read from an outer join's target: that row can be absent, and
    ///     <c>c.Name</c> on a null <c>c</c> is a <see cref="System.NullReferenceException" /> the moment
    ///     the provider evaluates it in memory. The generated read is guarded and yields the type's
    ///     default instead, which is what an outer join means.
    /// </remarks>
    public required bool NeedsNullGuard { get; init; }

    /// <summary>The fully qualified type of the result property, for the guarded read's default.</summary>
    public required string TypeFullName { get; init; }
}
