using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model representing a single [Projectable] property and its containing entity.
///     Produced by the transform — one per annotated expression-bodied property.
///     Entity info is included for grouping in the generator.
/// </summary>
internal sealed record ProjectablePropertyModel
{
    /// <summary>Entity namespace.</summary>
    public string EntityNamespace { get; init; } = "";

    /// <summary>Entity type name (simple).</summary>
    public required string EntityTypeName { get; init; }

    /// <summary>Full entity type name (namespace.type) for grouping.</summary>
    public string EntityFullTypeName => string.IsNullOrEmpty(EntityNamespace)
        ? EntityTypeName
        : $"{EntityNamespace}.{EntityTypeName}";

    /// <summary>Entity accessibility (public, internal, etc.).</summary>
    public required string EntityAccessibility { get; init; }

    /// <summary>Property name.</summary>
    public required string PropertyName { get; init; }

    /// <summary>Property return type (fully qualified via SymbolDisplayFormat).</summary>
    public required string ReturnType { get; init; }

    /// <summary>Rewritten expression body with parameter prefix (e.g., "e.SubTotal + e.Tax").</summary>
    public required string ExpressionBody { get; init; }

    /// <summary>
    ///     The same body over <see cref="Core.ProjectableBody.PortableSource" />: what the module publishes
    ///     for a projection compiled in another assembly.
    /// </summary>
    public required string PortableBody { get; init; }

    /// <summary>The specifications the body passes to a query that read the row: each is PRAG0735.</summary>
    public EquatableArray<SpecificationReadingTheRowModel> SpecificationsReadingTheRow { get; init; } =
        EquatableArray<SpecificationReadingTheRowModel>.Empty;
}
