// Pragmatic.SourceGenerator - Composition - Discovered Action/Mutation Info
// Models for actions and mutations discovered from referenced assembly metadata

using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     A DomainAction registration discovered from a referenced assembly's enriched metadata.
///     Contains the invoker type info needed for direct DI registration by the host.
/// </summary>
internal sealed record DiscoveredActionInfo
{
    /// <summary>Gets the fully qualified action type name.</summary>
    public required string ActionType { get; init; }

    /// <summary>Gets whether the action is void (no return value).</summary>
    public required bool IsVoid { get; init; }

    /// <summary>Gets the return type (null for void actions).</summary>
    public string? ReturnType { get; init; }

    /// <summary>Gets the fully qualified invoker type name (with global:: prefix).</summary>
    public required string InvokerType { get; init; }

    /// <summary>
    ///     The compensator declared by <c>[UndoWith&lt;T&gt;]</c>, or <c>null</c>. The host registers it
    ///     and the request scope that holds its undos: the module's own registration extension is never
    ///     called, so this is the only channel that reaches a running application.
    /// </summary>
    public string? CompensatorType { get; init; }

    /// <summary>Gets the source assembly name for grouping/comments.</summary>
    public required string SourceAssembly { get; init; }

    /// <summary>Gets the fully qualified policy type from [RequirePolicy&lt;T&gt;], or null.</summary>
    public string? PolicyTypeFqn { get; init; }

    /// <summary>Gets permission names from [RequirePermission] (all required).</summary>
    public EquatableArray<string> RequireAllPermissions { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Gets permission names from [RequireAnyPermission] (any required).</summary>
    public EquatableArray<string> RequireAnyPermissions { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Concrete step-invoker type names for a composite action's <b>mutation</b> steps.
    ///     Action steps are injected by interface and need nothing here.
    /// </summary>
    public EquatableArray<string> CompositeStepInvokers { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Whether this action is a composite with step properties of any kind, and therefore has a
    ///     <c>CompositeInvoker</c> the host must register.
    /// </summary>
    /// <remarks>
    ///     Read from the metadata rather than inferred from <see cref="CompositeStepInvokers" /> being
    ///     non-empty: a composite of action steps has a <c>CompositeInvoker</c> and an empty list.
    /// </remarks>
    public bool HasCompositeSteps { get; init; }

    /// <summary>
    ///     The boundary-keyed services this operation asks for <b>without</b> a key, because the
    ///     assembly that generated its invoker declares no boundary.
    /// </summary>
    /// <remarks>
    ///     Only a package produces these. The importing module names a boundary with
    ///     <c>[UsePackage&lt;TPackage, TBoundary&gt;]</c> and the generated registration bridges the
    ///     unkeyed request to that boundary's keyed instance — the key cannot be added to a constructor
    ///     that was compiled in the package.
    /// </remarks>
    public EquatableArray<string> BoundaryKeyedServices { get; init; } = EquatableArray<string>.Empty;
}

/// <summary>
///     A Mutation registration discovered from a referenced assembly's enriched metadata.
///     Contains the invoker type info needed for direct DI registration by the host.
/// </summary>
internal sealed record DiscoveredMutationInfo
{
    /// <summary>Gets the fully qualified mutation type name.</summary>
    public required string MutationType { get; init; }

    /// <summary>Gets the fully qualified entity type name.</summary>
    public required string EntityType { get; init; }

    /// <summary>Gets the fully qualified mutation invoker type name (with global:: prefix).</summary>
    public required string InvokerType { get; init; }

    /// <summary>
    ///     <c>"Id"</c> or <c>"LogicalKey"</c> when the mutation returns that instead of the entity;
    ///     <c>null</c> for the entity.
    /// </summary>
    public string? ReturnKind { get; init; }

    /// <summary>The compensator declared by <c>[UndoWith&lt;T&gt;]</c>, or <c>null</c>.</summary>
    public string? CompensatorType { get; init; }

    /// <summary>
    ///     Preset providers declared by <c>[PresetProvider&lt;T&gt;]</c> on the entity this mutation
    ///     creates. The generated invoker resolves each one from the container, so the host has to
    ///     register them.
    /// </summary>
    public EquatableArray<string> PresetProviders { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Gets the source assembly name for grouping/comments.</summary>
    public required string SourceAssembly { get; init; }

    /// <summary>Gets computed default generators from [ComputedDefault] attributes on entity properties.</summary>
    public EquatableArray<DiscoveredComputedDefaultInfo> ComputedDefaults { get; init; } = EquatableArray<DiscoveredComputedDefaultInfo>.Empty;

    /// <summary>Gets the fully qualified policy type from [RequirePolicy&lt;T&gt;], or null.</summary>
    public string? PolicyTypeFqn { get; init; }

    /// <summary>Gets permission names from [RequirePermission] (all required).</summary>
    public EquatableArray<string> RequireAllPermissions { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Gets permission names from [RequireAnyPermission] (any required).</summary>
    public EquatableArray<string> RequireAnyPermissions { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     The boundary-keyed services this operation asks for <b>without</b> a key, because the
    ///     assembly that generated its invoker declares no boundary.
    /// </summary>
    /// <remarks>
    ///     Only a package produces these. The importing module names a boundary with
    ///     <c>[UsePackage&lt;TPackage, TBoundary&gt;]</c> and the generated registration bridges the
    ///     unkeyed request to that boundary's keyed instance — the key cannot be added to a constructor
    ///     that was compiled in the package.
    /// </remarks>
    public EquatableArray<string> BoundaryKeyedServices { get; init; } = EquatableArray<string>.Empty;
}

/// <summary>
///     Represents a [ComputedDefault] generator discovered from metadata.
///     Used by the host to register IDefaultValueGenerator&lt;TEntity, TValue&gt; in DI.
/// </summary>
internal sealed record DiscoveredComputedDefaultInfo
{
    public required string EntityTypeFqn { get; init; }
    public required string ValueTypeFqn { get; init; }
    public required string GeneratorTypeFqn { get; init; }

    /// <summary>Whether the generator injects scoped services (a keyed DbContext) and must be registered
    /// as scoped rather than singleton — true for sequence-backed [GeneratedValue] {SEQ:N} formatters.</summary>
    public bool RequiresScope { get; init; }
}
