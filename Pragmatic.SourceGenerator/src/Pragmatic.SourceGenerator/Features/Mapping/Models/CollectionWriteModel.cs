namespace Pragmatic.SourceGenerator.Features.Mapping.Models;

/// <summary>
///     How a collection of DTOs is written back to the entity's collection, and by what the elements
///     are matched.
/// </summary>
/// <remarks>
///     <para>
///         The strategy is derived, not asked for. A <c>[Patch&lt;T&gt;]</c> is a delta, so a collection
///         it carries adds and updates without removing; a <c>[MapTo&lt;T&gt;]</c> is the whole
///         representation, so a collection it carries is the new state.
///         <c>[CollectionStrategy]</c> overrides both, for the domain rule the shape cannot express.
///     </para>
///     <para>
///         What is <b>not</b> derivable is the identity of an element: the DTO sends
///         <c>[a, b]</c> and the entity holds <c>[a, b, c]</c>, and matching them needs a key. The key
///         is looked for in the two places a developer has already put one — the element DTO's own id,
///         then the child entity's <c>[LogicKey]</c> — and when neither is there the operation is
///         reported rather than guessed at.
///     </para>
/// </remarks>
internal sealed record CollectionWriteModel
{
    /// <summary>The <c>CollectionStrategy</c> member name to pass to <c>MapOneToMany</c>.</summary>
    public required string Strategy { get; init; }

    /// <summary>Whether the strategy came from <c>[CollectionStrategy]</c> rather than from the shape.</summary>
    public bool IsExplicit { get; init; }

    /// <summary>The key property on the DTO element, when one was found.</summary>
    public string? DtoKeyProperty { get; init; }

    /// <summary>The matching key property on the entity element.</summary>
    public string? EntityKeyProperty { get; init; }

    /// <summary>What is missing, when the elements cannot be matched (PRAG0333).</summary>
    public CollectionKeyProblem KeyProblem { get; init; }

    /// <summary>Whether the elements can be matched, and therefore whether a keyed strategy can run.</summary>
    public bool CanMatchElements => DtoKeyProperty is not null && EntityKeyProperty is not null;
}

/// <summary>Why a collection's elements cannot be matched one to one.</summary>
internal enum CollectionKeyProblem
{
    /// <summary>Nothing wrong: a key was found, or none is needed.</summary>
    None,

    /// <summary>
    ///     Neither the element DTO nor the child entity offers something to match on.
    /// </summary>
    NoKey,

    /// <summary>
    ///     The element DTO has a key the child entity does not, or the other way round.
    /// </summary>
    KeyNotOnBothSides,
}
