using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for generating recursive CTE hierarchy queries: every tree the entity declares, and every
///     <c>[GenerateHierarchy]</c> that failed to resolve to one.
/// </summary>
internal sealed record HierarchyModel
{
    public required string Namespace { get; init; }
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }
    public required string IdType { get; init; }

    /// <summary>The trees that resolved: one per <c>[GenerateHierarchy]</c> that named, or had, its edge.</summary>
    public EquatableArray<HierarchyTreeModel> Trees { get; init; } = EquatableArray<HierarchyTreeModel>.Empty;

    /// <summary>The attributes that did not resolve — reported, never silently dropped.</summary>
    public EquatableArray<HierarchyProblemModel> Problems { get; init; } = EquatableArray<HierarchyProblemModel>.Empty;

    /// <summary>Where the attribute sits, so a diagnostic can point at it.</summary>
    public LocationInfo? LocationInfo { get; init; }

    public Location? Location => LocationInfo?.ToLocation();

    public bool IsValid => !string.IsNullOrEmpty(TypeName) && Trees.Length > 0;
}
