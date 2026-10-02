namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Represents a property to be generated in the {Entity}.Relations.g.cs file.
///     Produced by RelationGraphBuilder from cross-entity wiring.
/// </summary>
internal sealed record GeneratedRelationPropertyModel
{
    /// <summary>
    ///     The property kind: Collection, ForeignKey, ReferenceNav.
    /// </summary>
    public required RelationPropertyKind Kind { get; init; }

    /// <summary>
    ///     The property name (e.g., "LineItems", "InvoiceId", "Invoice").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     The property type name for code generation.
    ///     For Collection: the element type (e.g., "LineItem").
    ///     For ForeignKey: the FK type (e.g., "System.Guid").
    ///     For ReferenceNav: the target type (e.g., "Invoice").
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     The fully qualified type name for using directives.
    /// </summary>
    public required string FullTypeName { get; init; }

    /// <summary>
    ///     Whether this is a cross-boundary relation (FK only, no navigation).
    /// </summary>
    public bool IsCrossBoundary { get; init; }

    /// <summary>
    ///     Whether the relationship is optional, so the foreign key and the navigation are nullable.
    /// </summary>
    /// <remarks>
    ///     ⚠️ From <c>WithNavigation.Required = false</c>, which was read in <b>one</b> of the three
    ///     places that needed it: <c>RelationForeignKeyNaming.Type</c> made a many-to-one foreign key
    ///     nullable, while the generated <b>navigation</b> stayed
    ///     <c>public T Name { get; set; } = null!;</c> whatever the author wrote, and the one-to-one
    ///     foreign key ignored it outright. So an optional relation had a nullable column and a
    ///     navigation that promised never to be null — and assigning null to it, which is what
    ///     <c>[ReferenceStrategy(Detach)]</c> does, was <c>CS8601</c> inside a generated file.
    /// </remarks>
    public bool IsOptional { get; init; }

    /// <summary>
    ///     XML doc summary for the property.
    /// </summary>
    public string? Summary { get; init; }
}

/// <summary>
///     The kind of generated relation property.
/// </summary>
internal enum RelationPropertyKind
{
    /// <summary>ICollection&lt;T&gt; property with empty initializer.</summary>
    Collection,

    /// <summary>Foreign key property with private setter.</summary>
    ForeignKey,

    /// <summary>Reference navigation property with public setter.</summary>
    ReferenceNav
}
