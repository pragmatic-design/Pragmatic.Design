namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     A grouping specification in a <see cref="GridFilterRequest" />.
/// </summary>
/// <param name="Field">The entity property name to group by.</param>
/// <param name="SortDirection">Optional sort direction for the grouped field.</param>
public record GroupClause(string Field, SortDirection? SortDirection = null);
