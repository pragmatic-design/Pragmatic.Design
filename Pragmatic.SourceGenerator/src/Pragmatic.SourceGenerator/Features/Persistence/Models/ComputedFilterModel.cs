using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for an entity that has one or more [ComputedFilter] properties.
///     Groups all computed filter properties for a single entity type.
/// </summary>
internal sealed record ComputedFilterModel
{
    public required string Namespace { get; init; }
    public required string TypeName { get; init; }
    public required string Accessibility { get; init; }

    public string FullTypeName => string.IsNullOrEmpty(Namespace) ? TypeName : $"{Namespace}.{TypeName}";

    /// <summary>The [ComputedFilter] boolean properties on this entity.</summary>
    public required EquatableArray<ComputedFilterPropertyModel> Properties { get; init; }

    public bool IsValid => !string.IsNullOrEmpty(TypeName) && Properties.Length > 0;
}

/// <summary>
///     Model for a single [ComputedFilter] boolean property.
/// </summary>
internal sealed record ComputedFilterPropertyModel
{
    public required string PropertyName { get; init; }

    /// <summary>The rewritten expression body (e.g., "e.DueDate &lt; ...").</summary>
    public required string ExpressionBody { get; init; }

    /// <summary>
    ///     For a method, its parameters as the generated members declare them
    ///     (<c>global::System.DateOnly day</c>); null for a property.
    /// </summary>
    public string? Parameters { get; init; }

    /// <summary>For a method, the same parameters passed on as arguments (<c>day</c>).</summary>
    public string? Arguments { get; init; }

    /// <summary>Whether the filter is a method, whose specification is built for the values it is given.</summary>
    public bool IsMethod => Parameters is not null;
}
