using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model representing a grid adapter to be generated.
/// </summary>
internal sealed record GridAdapterModel
{
    public required string Namespace { get; init; }
    public required string TypeName { get; init; }
    public required string Accessibility { get; init; }
    public required string EntityType { get; init; }
    public required string EntityTypeFullName { get; init; }
    public required GridFrameworkFlags Framework { get; init; }
    public required bool SupportNestedFilters { get; init; }
    public required bool SupportGrouping { get; init; }
    public required EquatableArray<GridFieldModel> Fields { get; init; }
    public required EquatableArray<string> ExcludedProperties { get; init; }
}

/// <summary>
///     Flags for grid framework support.
/// </summary>
[Flags]
internal enum GridFrameworkFlags
{
    None = 0,
    DevExpress = 1,
    PrimeNG = 2,
    Both = DevExpress | PrimeNG
}

/// <summary>
///     Model representing a field mapping in the grid adapter.
/// </summary>
internal sealed record GridFieldModel
{
    /// <summary>
    ///     The field name as it appears in the JSON from frontend.
    ///     This is either from [GridField] attribute or auto-derived from property name.
    /// </summary>
    public required string JsonField { get; init; }

    /// <summary>
    ///     The entity property path (e.g., "Status", "Customer.Name").
    /// </summary>
    public required string PropertyPath { get; init; }

    /// <summary>
    ///     The property name for the generated method (e.g., "Status", "CustomerName").
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The CLR type of the property (e.g., "int", "string", "OrderStatus").
    /// </summary>
    public required string PropertyType { get; init; }

    /// <summary>
    ///     The full CLR type including namespace.
    /// </summary>
    public required string PropertyTypeFullName { get; init; }

    /// <summary>
    ///     Whether this is a nullable type.
    /// </summary>
    public required bool IsNullable { get; init; }

    /// <summary>
    ///     The underlying type for nullable types (e.g., "int" for "int?").
    /// </summary>
    public required string UnderlyingType { get; init; }

    /// <summary>
    ///     The fully qualified underlying type for use in generated code.
    /// </summary>
    public required string UnderlyingTypeFullName { get; init; }

    /// <summary>
    ///     The category of the property type for operator selection.
    /// </summary>
    public required PropertyTypeCategory TypeCategory { get; init; }

    /// <summary>
    ///     Whether this is a nested property (contains ".").
    /// </summary>
    public required bool IsNested { get; init; }

    /// <summary>
    ///     Whether this field supports filtering.
    /// </summary>
    public required bool Filterable { get; init; }

    /// <summary>
    ///     Whether this field supports sorting.
    /// </summary>
    public required bool Sortable { get; init; }

    /// <summary>
    ///     Whether this field supports grouping.
    /// </summary>
    public required bool Groupable { get; init; }

    /// <summary>
    ///     Specific operators allowed (null = all valid for type).
    /// </summary>
    public required EquatableArray<string>? AllowedOperators { get; init; }
}

/// <summary>
///     Categories of property types for determining valid operators.
/// </summary>
internal enum PropertyTypeCategory
{
    /// <summary>String type - supports contains, startsWith, endsWith.</summary>
    String,

    /// <summary>Numeric types (int, decimal, etc.) - supports comparison operators.</summary>
    Numeric,

    /// <summary>Boolean type - supports equals only.</summary>
    Boolean,

    /// <summary>DateTime/DateTimeOffset - supports comparison operators.</summary>
    DateTime,

    /// <summary>Guid - supports equals only.</summary>
    Guid,

    /// <summary>Enum type - supports equals, notEquals, in.</summary>
    Enum,

    /// <summary>Collection type - supports contains, any.</summary>
    Collection,

    /// <summary>Unknown/unsupported type.</summary>
    Unknown
}
