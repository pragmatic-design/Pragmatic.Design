namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     A list of keys on a mutation that chooses which rows a navigation points at — <c>[LinkIds]</c>.
/// </summary>
/// <remarks>
///     ⚠️ Not a child, and not a value either. A child is merged by <c>ApplyToEntity</c>; a value is
///     assigned there. Linking a row named only by its key means attaching it to the change tracker,
///     which needs a <c>DbContext</c> — and <c>ApplyToEntity(entity)</c> has none, by design: Actions
///     stays free of EF. So the invoker does it, through a capability the repository implements, the
///     same shape <c>INavigationLoader</c> already has.
/// </remarks>
internal sealed record MutationLinkModel
{
    /// <summary>The property on the mutation carrying the keys.</summary>
    public required string PropertyName { get; init; }

    /// <summary>The navigation on the entity whose links it chooses.</summary>
    public required string Navigation { get; init; }

    /// <summary>The related entity's key property.</summary>
    public required string Key { get; init; }

    /// <summary>How the set of links is written: one of <c>CollectionStrategy</c>.</summary>
    public required string Strategy { get; init; }

    /// <summary>
    ///     The entity on the other side of the navigation, fully qualified.
    /// </summary>
    /// <remarks>
    ///     Generated code names it twice: as the type argument that keeps the write out of reflection,
    ///     and as the factory that builds the stub. Resolved from the ENTITY's navigation, not from
    ///     the mutation's property — the caller sends keys, so its own type says nothing about what is
    ///     on the other side.
    /// </remarks>
    public required string RelatedEntityFullTypeName { get; init; }
}
