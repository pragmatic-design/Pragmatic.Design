namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Marks a property as filterable: on a <see cref="GridFilterAttribute{TEntity}" /> class, and on
///     an entity carrying <see cref="GenerateGridBridgeAttribute" />.
/// </summary>
/// <remarks>
///     <para>
///         <b>On an entity it is the allowlist.</b> The generated grid bridge names only the properties
///         that declare this, because the field name comes from the client: a list of what is forbidden
///         covers whatever somebody remembered to put in it and exposes the rest, and sorting or
///         filtering on a column makes it talk without reading it. A property that declares nothing is
///         answered as unknown; <see cref="GridExcludeAttribute" /> on the entity takes back one that
///         is declared. The framework's reserved columns — credentials, <c>OwnerId</c>,
///         <c>TenantId</c>, <c>AccessScopes</c> — are withheld even when declared.
///     </para>
///     <para>
///         For string properties, a companion property {Name}Operator of type
///         <see cref="StringOperator" /> can be defined to allow dynamic operator selection.
///     </para>
///     <para>
///         For range filtering (Min/Max), define two properties with appropriate names
///         and set the <see cref="Operators" /> to include <see cref="FilterOps.Range" />.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FilterableAttribute : Attribute
{
    /// <summary>
    ///     The allowed operators for this filter.
    ///     Default is <see cref="FilterOps.All" />.
    /// </summary>
    public FilterOps Operators { get; set; } = FilterOps.All;

    /// <summary>
    ///     The target property path on the entity.
    ///     If not specified, uses the same name as the filter property.
    /// </summary>
    public string? MapTo { get; set; }

    /// <summary>
    ///     Custom filter handler type for special filtering logic.
    ///     Must implement IFilterHandler&lt;TEntity&gt;.
    /// </summary>
    public Type? Handler { get; set; }

    /// <summary>
    ///     Arguments to pass to the custom handler constructor.
    ///     Typically used for JSON path or other configuration.
    /// </summary>
    public string? HandlerArgs { get; set; }
}
