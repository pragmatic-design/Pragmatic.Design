using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for generating a loading profile — controls navigation includes and split query hints.
/// </summary>
internal sealed record LoadingProfileModel
{
    public required string Namespace { get; init; }
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }
    public required string EntityTypeName { get; init; }
    public required string EntityFullTypeName { get; init; }

    /// <summary>Maximum depth of navigation includes.</summary>
    public int MaxDepth { get; init; } = 1;

    /// <summary>Whether to use split queries for collection navigations.</summary>
    public bool SplitQuery { get; init; }

    /// <summary>Navigation paths to include (dot-separated for nested, e.g., "Rooms.LineItems").</summary>
    public EquatableArray<string> NavigationPaths { get; init; } = EquatableArray<string>.Empty;

    /// <summary>DTO properties that look like navigations but don't match any entity navigation.</summary>
    public EquatableArray<string> UnmatchedNavigationNames { get; init; } = EquatableArray<string>.Empty;

    public bool IsValid => !string.IsNullOrEmpty(EntityTypeName);
    public bool HasNavigations => !NavigationPaths.IsDefaultOrEmpty;
    public bool HasUnmatchedNavigations => !UnmatchedNavigationNames.IsDefaultOrEmpty;
}
