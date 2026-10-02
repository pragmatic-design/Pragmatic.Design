namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     A single filter condition in a <see cref="GridFilterRequest" />.
/// </summary>
/// <param name="Field">The entity property name to filter on.</param>
/// <param name="Operator">The comparison operator.</param>
/// <param name="Value">The filter value (may be null for "is null" checks).</param>
/// <param name="Logic">
///     The logical connector to the next filter clause. Default is <see cref="FilterLogic.And" />.
/// </param>
public record FilterClause(
    string Field,
    FilterOperator Operator,
    object? Value,
    FilterLogic Logic = FilterLogic.And);
