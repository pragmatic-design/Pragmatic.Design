using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Represents a cascade update relationship:
///     when SourceType.SourceProperty changes, update TargetType.TargetProperty.
/// </summary>
internal sealed record CascadeModel
{
    /// <summary>The namespace for the generated handler class.</summary>
    public required string Namespace { get; init; }

    /// <summary>The source entity type short name (e.g., "RoomType").</summary>
    public required string SourceTypeName { get; init; }

    /// <summary>The source entity FQN without global:: prefix for matching (e.g., "Contoso.Sales.RoomType").</summary>
    public required string SourceFullTypeName { get; init; }

    /// <summary>The source entity FQN with global:: prefix for code generation (e.g., "global::Contoso.Sales.RoomType").</summary>
    public required string SourceQualifiedTypeName { get; init; }

    /// <summary>The source property name that triggers the cascade (e.g., "Price").</summary>
    public required string SourceProperty { get; init; }

    /// <summary>The target entity type short name (e.g., "LineItem").</summary>
    public required string TargetTypeName { get; init; }

    /// <summary>The target entity FQN (e.g., "global::Contoso.Sales.LineItem").</summary>
    public required string TargetFullTypeName { get; init; }

    /// <summary>The target property that receives the cascaded value (e.g., "UnitPrice").</summary>
    public required string TargetProperty { get; init; }

    /// <summary>
    ///     The target property's CLR type FQN (e.g., "decimal"). The cascaded value arrives boxed as
    ///     <c>object?</c> on the event, so ExecuteUpdate's SetProperty must cast it back to this concrete
    ///     type — otherwise the relational provider cannot assign a type mapping to the parameter.
    /// </summary>
    public required string TargetPropertyTypeName { get; init; }

    /// <summary>The FK property name on the target entity pointing to the source (e.g., "RoomTypeId").</summary>
    public required string ForeignKeyProperty { get; init; }

    /// <summary>The FK property type name (e.g., "System.Guid", "System.Guid?").</summary>
    public string ForeignKeyTypeName { get; init; } = "object";

    /// <summary>
    ///     The FQN of the target entity's boundary (e.g., "Showcase.Billing.Entities.BillingBoundary"),
    ///     used to inject the correct keyed <c>DbContext</c> in host mode. Null when the target has no
    ///     resolvable boundary (console mode falls back to the non-keyed DbContext).
    /// </summary>
    public string? TargetBoundaryTypeFullName { get; init; }

    /// <summary>Optional SQL-translatable boolean property on the target used to filter which rows cascade.</summary>
    public string? Condition { get; init; }

    /// <summary>
    ///     Whether the target entity really declares the foreign key the handler filters on.
    /// </summary>
    /// <remarks>
    ///     The handler finds its rows with <c>Where(e => e.{Source}Id == entityId)</c>. When the target
    ///     has no such property, going on with a fallback type of <c>object</c> would make the generated
    ///     handler name a member that does not exist — CS1061 inside a file its author cannot edit.
    ///     This flag is the check.
    /// </remarks>
    public bool HasForeignKey { get; init; }

    /// <summary>Where the [CascadeOn] property is declared, for the diagnostic that names it.</summary>
    public LocationInfo? Location { get; init; }

    public bool IsValid => !string.IsNullOrEmpty(SourceProperty) && !string.IsNullOrEmpty(TargetProperty);
}
