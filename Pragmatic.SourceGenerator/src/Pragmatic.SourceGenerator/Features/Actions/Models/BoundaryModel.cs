using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

internal sealed record BoundaryModel
{
    public required string Namespace { get; init; }
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }
    public required string InterfaceName { get; init; }
    public required string InternalInterfaceName { get; init; }
    public required string ImplementationName { get; init; }
    public required string Accessibility { get; init; }
    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();
    public bool IsValid => InvalidReason == BoundaryInvalidReason.None;

    /// <summary>
    ///     The boundary declares <c>[Transactional]</c>, which is not something a boundary can deliver
    ///     (PRAG0431). Reported here, once, rather than on each of its actions.
    /// </summary>
    public bool DeclaresTransactional { get; init; }
    public BoundaryInvalidReason InvalidReason { get; init; }
    public EquatableArray<string> ReadAccessTypes { get; init; } = EquatableArray<string>.Empty;
    public EquatableArray<SubBoundaryModel> SubBoundaries { get; init; } = EquatableArray<SubBoundaryModel>.Empty;
    public bool HasSubBoundaries => !SubBoundaries.IsDefaultOrEmpty;

    /// <summary>
    ///     Whether the boundary has <c>Visibility = BoundaryVisibility.Internal</c>.
    ///     When true, only the internal interface is generated (no public interface).
    /// </summary>
    public bool IsInternal { get; init; }

    /// <summary>
    ///     True when no <c>[Boundary]</c> was declared and this one was derived from the module.
    /// </summary>
    /// <remarks>
    ///     The class itself has to be emitted in that case; a declared boundary already exists in
    ///     source and emitting it again would be a duplicate definition.
    /// </remarks>
    public bool IsGenerated { get; init; }

    /// <summary>
    ///     The boundary named at this module's <c>[UsePackage&lt;TPackage, TBoundary&gt;]</c>, when an
    ///     imported operation needs a boundary-keyed service. <c>null</c> otherwise.
    /// </summary>
    /// <remarks>
    ///     A package generates its invokers where no boundary exists, so they ask for <c>DbContext</c>
    ///     and <c>IUnitOfWork</c> unkeyed and their constructors are fixed before any importer sees
    ///     them. This is the importer's answer, and the generated registration bridges the unkeyed
    ///     request to that boundary's keyed instance.
    /// </remarks>
    public string? PackageBoundaryKey { get; init; }
}

internal enum BoundaryInvalidReason
{
    None,
    NotPartial,
    NoNamespace
}
