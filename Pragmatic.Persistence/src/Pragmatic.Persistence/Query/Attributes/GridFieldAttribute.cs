namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Configures a field mapping for the grid adapter.
///     Use this to map JSON field names to entity properties,
///     especially for nested properties or custom aliases.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class GridFieldAttribute : Attribute
{
    /// <summary>
    ///     Creates a grid field mapping.
    /// </summary>
    /// <param name="jsonField">The field name as it appears in the JSON from frontend.</param>
    public GridFieldAttribute(string jsonField)
    {
        JsonField = jsonField;
    }

    /// <summary>
    ///     The field name as it appears in the JSON from frontend.
    ///     Example: "customerName", "created_at"
    /// </summary>
    public string JsonField { get; }

    /// <summary>
    ///     The entity property path to map to.
    ///     If not specified, uses the JSON field name with PascalCase conversion.
    ///     Example: "Customer.Name", "CreatedAt"
    /// </summary>
    public string? Property { get; set; }

    /// <summary>
    ///     Whether this field supports filtering.
    ///     Default is true.
    /// </summary>
    public bool Filterable { get; set; } = true;

    /// <summary>
    ///     Whether this field supports sorting.
    ///     Default is true.
    /// </summary>
    public bool Sortable { get; set; } = true;

    /// <summary>
    ///     Whether this field supports grouping.
    ///     Default is true.
    /// </summary>
    public bool Groupable { get; set; } = true;

    /// <summary>
    ///     Specific operators allowed for this field.
    ///     If not specified, all operators valid for the property type are allowed.
    ///     Example: new[] { "=", "!=", "contains" }
    /// </summary>
    public string[]? AllowedOperators { get; set; }
}
