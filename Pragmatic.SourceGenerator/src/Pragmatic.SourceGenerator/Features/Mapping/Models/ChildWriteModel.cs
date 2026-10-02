namespace Pragmatic.SourceGenerator.Features.Mapping.Models;

/// <summary>
///     One child of an aggregate, and everything needed to write it back.
/// </summary>
/// <remarks>
///     The same child write is emitted from two places — a patch's <c>ApplyPatch</c> and a mutation's
///     <c>ApplyToEntity</c> — over two different transforms. This is the shape they agree on, so the
///     rule that turns it into code lives once.
/// </remarks>
internal sealed record ChildWriteModel
{
    /// <summary>The property on the DTO or mutation that carries the child.</summary>
    public required string PropertyName { get; init; }

    /// <summary>The navigation on the entity it is written to.</summary>
    public required string TargetPropertyName { get; init; }

    /// <summary>Whether the property holds many children or one.</summary>
    public bool IsCollection { get; init; }

    /// <summary>How the collection is merged, and what its elements are matched by.</summary>
    public CollectionWriteModel? Collection { get; init; }

    /// <summary>
    ///     The child is itself a <c>[Mutation]</c>: it is written through its own
    ///     <c>ApplyToEntity</c>, and a new one is <c>new TEntity()</c> filled by the same call.
    /// </summary>
    /// <remarks>
    ///     It needs no mapping attribute because <c>ApplyToEntity</c> is <c>virtual</c> on
    ///     <c>Mutation&lt;TEntity&gt;</c> — nameable before any generator writes the override, which is
    ///     the constraint that forced DTO children to be recognised by attribute.
    /// </remarks>
    public bool IsChildMutation { get; init; }

    /// <summary>The entity a new child is constructed as. Set only when <see cref="IsChildMutation" />.</summary>
    public string? ChildEntityFullTypeName { get; init; }

    /// <summary>The child entity's factory takes no parameters, so it can be built through it.</summary>
    public bool ChildEntityHasParameterlessFactory { get; init; }

    /// <summary>The child DTO can build a new entity — <c>[MapTo&lt;T&gt;]</c> gives it <c>ToEntity()</c>.</summary>
    public bool CanCreate { get; init; }

    /// <summary>The child DTO can update one through <c>ApplyPatch()</c> — it is a <c>[Patch&lt;T&gt;]</c>.</summary>
    public bool CanPatch { get; init; }

    /// <summary>
    ///     How a child that is one, not many, is written: <c>Merge</c>, <c>Detach</c>, <c>Replace</c>
    ///     or <c>Ignore</c>. Ignored for a collection, which has its own four in
    ///     <see cref="Collection" />.
    /// </summary>
    public string ReferenceStrategy { get; init; } = "Merge";

    /// <summary>The navigation's setter is not public, so it is written through the generated <c>Set{Name}</c>.</summary>
    public bool EntityHasPrivateSetter { get; init; }
}
