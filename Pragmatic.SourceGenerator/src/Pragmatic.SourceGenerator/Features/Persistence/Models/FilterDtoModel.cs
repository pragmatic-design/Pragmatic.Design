using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model representing a [FilterDto&lt;TEntity&gt;] class.
///     Used for incremental generator caching.
/// </summary>
internal sealed record FilterDtoModel
{
    public string Namespace { get; init; } = "";

    public required string TypeName { get; init; }

    public string FullTypeName => string.IsNullOrEmpty(Namespace) ? TypeName : $"{Namespace}.{TypeName}";

    public required string Accessibility { get; init; }

    public required string TypeKind { get; init; }

    public bool IsRecord { get; init; }

    public required string EntityTypeFullName { get; init; }

    public required string EntityTypeName { get; init; }

    /// <summary>
    ///     Properties marked with [Filter].
    /// </summary>
    public EquatableArray<FilterDtoPropertyModel> Filters { get; init; } =
        EquatableArray<FilterDtoPropertyModel>.Empty;

    /// <summary>
    ///     Properties marked with [FilterGroup].
    /// </summary>
    public EquatableArray<FilterDtoGroupModel> Groups { get; init; } =
        EquatableArray<FilterDtoGroupModel>.Empty;

    public bool HasFilters => Filters.Length > 0;

    public bool HasGroups => Groups.Length > 0;

    public bool IsValid => !string.IsNullOrEmpty(TypeName) && !string.IsNullOrEmpty(EntityTypeFullName);
}
