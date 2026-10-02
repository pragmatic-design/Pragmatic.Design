namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Marks a property as a filter condition in a <see cref="FilterDtoAttribute{TEntity}" /> class.
/// </summary>
/// <remarks>
///     <para>
///         When the property value is null, the filter is not applied (skip behavior).
///     </para>
///     <para>
///         For string properties, the default operator is <see cref="FilterOperator.Contains" />.
///         For other types, the default is <see cref="FilterOperator.Equals" />.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FilterAttribute : Attribute
{
    /// <summary>
    ///     The comparison operator. Default is Contains for strings, Equals for others.
    /// </summary>
    public FilterOperator Operator { get; set; } = FilterOperator.Equals;

    /// <summary>
    ///     The target property path on the entity.
    ///     If not specified, uses the same name as the DTO property.
    ///     Supports nested paths like "Customer.Name" or "Address.City".
    /// </summary>
    public string? MapTo { get; set; }

    /// <summary>
    ///     Whether to use case-insensitive string comparison.
    ///     Only applies to string operators.
    /// </summary>
    public bool IgnoreCase { get; set; }
}
