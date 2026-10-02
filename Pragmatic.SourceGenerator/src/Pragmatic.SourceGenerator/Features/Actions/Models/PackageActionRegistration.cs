using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     Represents a package action invoker registration to include in the boundary extension.
///     These are actions from [UsePackage&lt;T&gt;] that belong to the boundary as if declared locally.
/// </summary>
internal sealed record PackageActionRegistration
{
    /// <summary>FQN of the action type (with global:: prefix).</summary>
    public required string ActionType { get; init; }

    /// <summary>FQN of the invoker type (with global:: prefix).</summary>
    public required string InvokerType { get; init; }

    /// <summary>Whether this is a void action (VoidDomainAction).</summary>
    public bool IsVoid { get; init; }

    /// <summary>Return type FQN (null for void actions).</summary>
    public string? ReturnType { get; init; }

    /// <summary>Whether this is a mutation.</summary>
    public bool IsMutation { get; init; }

    /// <summary>Entity type FQN for mutations.</summary>
    public string? EntityType { get; init; }

    /// <summary>
    ///     What the package's mutation returns when it is not the entity (<c>"Id"</c>,
    ///     <c>"LogicalKey"</c>), so the importer's member returns the same thing; <c>null</c> otherwise.
    /// </summary>
    public string? MutationReturnKind { get; init; }

    /// <summary>
    ///     The boundary-keyed services this operation's invoker asks for without a key.
    /// </summary>
    /// <remarks>
    ///     Declared by the package, because only the package knows; answered by the importer, because
    ///     only the importer has a boundary. See <c>UsePackageAttribute&lt;TPackage, TBoundary&gt;</c>.
    /// </remarks>
    public EquatableArray<string> BoundaryKeyedServices { get; init; } = EquatableArray<string>.Empty;

    /// <summary>The package assembly this operation came from.</summary>
    /// <remarks>
    ///     A module may import several packages, and the boundary is named per import. Without this,
    ///     one import naming a boundary would answer for a different package that named none.
    /// </remarks>
    public string? SourceAssembly { get; init; }
}
