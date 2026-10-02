using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     A child of the aggregate that a mutation carries, and whether it is allowed to write it.
/// </summary>
internal sealed record MutationChildModel
{
    /// <summary>The property on the mutation.</summary>
    public required string PropertyName { get; init; }

    /// <summary>The navigation on the entity it writes to.</summary>
    public required string TargetPropertyName { get; init; }

    /// <summary>The child entity's simple name.</summary>
    public required string ChildTypeName { get; init; }

    /// <summary>The DTO the mutation carries the child as.</summary>
    public required string ChildDtoTypeName { get; init; }

    /// <summary>The child DTO, fully qualified, for naming its generated statics.</summary>
    /// <remarks>
    ///     ⚠️ Needed to include what the child <b>writes</b>. A merge reads the collection it is
    ///     merging into, and EF cannot tell an empty collection from one that was never loaded:
    ///     measured, a load without the include turned two rows into four, silently, on a write
    ///     that sent the same two back.
    /// </remarks>
    public required string ChildDtoFullTypeName { get; init; }

    /// <summary>The entity the mutation operates on.</summary>
    public required string ParentTypeName { get; init; }

    /// <summary>
    ///     How a child that is one, not many, is written — from <c>[ReferenceStrategy]</c> on the
    ///     mutation's property. <c>Merge</c> unless the author said otherwise.
    /// </summary>
    public string ReferenceStrategy { get; init; } = "Merge";

    /// <summary>The boundary the parent entity belongs to, or null when it declares none.</summary>
    public string? ParentBoundaryName { get; init; }

    /// <summary>The boundary the child entity belongs to, or null when it declares none.</summary>
    /// <remarks>
    ///     A boundary is the transaction boundary. A child on the other side of one is written by the
    ///     parent's unit of work, through a <c>DbSet</c> that <c>[ReadAccess]</c> made reachable —
    ///     measured: that write arrives at the owner's row. Compared with
    ///     <see cref="ParentBoundaryName" /> only when both are known, so an entity that declares no
    ///     boundary is not accused of crossing one.
    /// </remarks>
    public string? ChildBoundaryName { get; init; }

    /// <summary>
    ///     The parent the child declares with <c>[PartOf&lt;TParent&gt;]</c>, or null when it declares
    ///     none — in which case the mutation may not write it.
    /// </summary>
    public string? DeclaredParentTypeName { get; init; }

    /// <summary>Whether the property holds many children or one.</summary>
    public bool IsCollection { get; init; }

    /// <summary>
    ///     The child's own <c>[Invariant]</c> methods, which the aggregate's invoker checks after it has
    ///     merged it.
    /// </summary>
    /// <remarks>
    ///     A <c>[PartOf]</c> child is written through its parent and has no operations of its own, so
    ///     this is the only path its rules can be checked on. They were checked on none: the rule read
    ///     as enforced and enforced nothing.
    /// </remarks>
    public EquatableArray<InvariantModel> Invariants { get; init; } = EquatableArray<InvariantModel>.Empty;

    /// <summary>Whether it has a rule of its own to be asked about.</summary>
    public bool HasInvariants => !Invariants.IsDefaultOrEmpty;

    /// <summary>How the collection is merged, and what its elements are matched by.</summary>
    public CollectionWriteModel? Collection { get; init; }

    /// <summary>
    ///     The child is itself a <c>[Mutation]</c>, so it is written through its own
    ///     <c>ApplyToEntity</c> and carries its own validation and permissions.
    /// </summary>
    public bool IsChildMutation { get; init; }

    /// <summary>The entity on the other side, fully qualified — what a new child is constructed as.</summary>
    public string? ChildEntityFullTypeName { get; init; }

    /// <summary>
    ///     The child entity's generated <c>Create()</c> takes no parameters, so a new child can be
    ///     built through the factory rather than the constructor — same reason as the root.
    /// </summary>
    public bool ChildEntityHasParameterlessFactory { get; init; }

    /// <summary>
    ///     The child can build a new entity: a <c>[MapTo&lt;T&gt;]</c> DTO through <c>ToEntity()</c>,
    ///     a child mutation through <c>new TEntity()</c> plus its own <c>ApplyToEntity</c>.
    /// </summary>
    public bool CanCreate { get; init; }

    /// <summary>The DTO can update one through <c>ApplyPatch()</c> — it declares <c>[Patch&lt;T&gt;]</c>.</summary>
    public bool CanPatch { get; init; }

    /// <summary>The navigation's setter is not public, so it is written through the generated setter.</summary>
    public bool EntityHasPrivateSetter { get; init; }

    /// <summary>Whether the entity has a navigation of this name at all.</summary>
    public bool EntityPropertyExists { get; init; }

    /// <summary>
    ///     The entity the target navigation holds, when it could be resolved — for comparing against
    ///     the one the child actually writes.
    /// </summary>
    public string? NavigationElementTypeName { get; init; }

    /// <summary>
    ///     The child writes the entity the navigation holds.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Distinct from <see cref="IsPartOfThisAggregate" />, and the difference is what
    ///     <c>PRAG0443</c> catches: <c>[PartOf]</c> says who may write the child, the navigation says
    ///     where its rows live. Two children of the same aggregate answer the first question the same
    ///     way, so only the second separates them.
    /// </remarks>
    public bool MatchesTheNavigation =>
        NavigationElementTypeName is null || NavigationElementTypeName == ChildTypeName;

    /// <summary>
    ///     Whether the child is declared part of exactly this aggregate.
    /// </summary>
    public bool IsPartOfThisAggregate => DeclaredParentTypeName == ParentTypeName;

    /// <summary>
    ///     Whether the elements can be matched against the children already there.
    /// </summary>
    public bool ElementsCanBeMatched =>
        !IsCollection || Collection is { KeyProblem: CollectionKeyProblem.None };

    /// <summary>
    ///     Whether the generator can emit the write.
    /// </summary>
    public bool IsWritable =>
        EntityPropertyExists && IsPartOfThisAggregate && MatchesTheNavigation
        && ElementsCanBeMatched && (CanCreate || CanPatch);
}
